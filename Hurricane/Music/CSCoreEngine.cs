﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CSCore;
using CSCore.Codecs;
using CSCore.CoreAudioAPI;
using CSCore.SoundOut;
using CSCore.SoundOut.DirectSound;
using CSCore.Streams;
using Hurricane.Music.Data;
using Hurricane.Music.MusicDatabase.EventArgs;
using Hurricane.Music.Visualization;
using Hurricane.Settings;
using Hurricane.ViewModelBase;
using WPFSoundVisualizationLib;
using Equalizer = CSCore.Streams.Equalizer;

namespace Hurricane.Music
{
    public class CSCoreEngine : PropertyChangedBase, IDisposable, ISpectrumPlayer
    {
        #region Consts
        const int FFTSize = 4096;
        const double MaxDB = 20;

        #endregion

        #region Events
        public event EventHandler StartVisualization;
        public event EventHandler TrackFinished;
        public event EventHandler<TrackChangedEventArgs> TrackChanged;
        public event EventHandler<PlayStateChangedEventArgs> PlaybackStateChanged;
        public event EventHandler<PositionChangedEventArgs> PositionChanged;
        public event EventHandler VolumeChanged;

        #endregion

        #region Properties
        private float _volume = 1.0f;
        public float Volume
        {
            get { return _volume; }
            set
            {
                if (SetProperty(value, ref _volume))
                {
                    if (_soundOut != null && _soundOut.WaveSource != null)
                        _soundOut.Volume = value;
                    if (VolumeChanged != null) VolumeChanged(this, EventArgs.Empty);
                }
            }
        }

        public long Position
        {
            get { return SoundSource == null ? 0 : SoundSource.Position; }
            set
            {
                if (SoundSource != null)
                    SoundSource.Position = value;
                OnPropertyChanged("CurrentTrackPosition");
                if (PositionChanged != null) PositionChanged(this, new PositionChangedEventArgs((int)this.CurrentTrackPosition.TotalSeconds, (int)this.CurrentTrackLength.TotalSeconds));
            }
        }

        private Track _currenttrack;
        public Track CurrentTrack
        {
            get { return _currenttrack; }
            protected set
            {
                if (SetProperty(value, ref _currenttrack))
                {
                    if (TrackChanged != null && _currenttrack != null) TrackChanged(this, new TrackChangedEventArgs(value));
                }
            }
        }

        public long TrackLength
        {
            get { return SoundSource == null ? 0 : SoundSource.Length; }
        }

        public bool IsPlaying
        {
            get { return (_soundOut != null && (!_isfadingout && _soundOut.PlaybackState == PlaybackState.Playing)); }
        }

        public PlaybackState CurrentState
        {
            get
            {
                if (_soundOut == null) { return PlaybackState.Stopped; } else { return _soundOut.PlaybackState; }
            }
        }

        public TimeSpan CurrentTrackPosition
        {
            get
            {
                try
                {
                    return SoundSource != null ? SoundSource.GetPosition() : TimeSpan.Zero;
                }
                catch (Exception)
                {
                    return TimeSpan.Zero; //Sometimes it crashes
                }
            }
        }

        public TimeSpan CurrentTrackLength
        {
            get
            {
                try
                {
                    return SoundSource != null ? SoundSource.GetLength() : TimeSpan.Zero;
                }
                catch (Exception)
                {
                    return TimeSpan.Zero;
                }
            }
        }

        public IWaveSource SoundSource { get; protected set; }

        public ConfigSettings Settings { get { return HurricaneSettings.Instance.Config; } }
        #endregion

        #region Members
        private string _currentDeviceId;
        protected ISoundOut _soundOut;
        protected MMNotificationClient _client;
        protected bool _manualstop;
        protected VolumeFading _fader;
        protected bool _isfadingout;
        protected Crossfade _Crossfade;

        #endregion

        #region Equalizer
        public Equalizer MusicEqualizer { get; set; }

        protected EqualizerSettings _equalizersettings;
        public EqualizerSettings EqualizerSettings { get { return _equalizersettings; } set { _equalizersettings = value; value.EqualizerChanged += value_EqualizerChanged; } }

        void value_EqualizerChanged(object sender, EqualizerChangedEventArgs e)
        {
            SetEqualizerValue(e.EqualizerValue, e.EqualizerNumber);
        }

        protected void SetEqualizerValue(double value, int number)
        {
            if (MusicEqualizer == null) return;
            double perc = (value / 100);
            var newvalue = (float)(perc * MaxDB);

            //the tag of the trackbar contains the index of the filter
            EqualizerFilter filter = MusicEqualizer.SampleFilters[number];
            filter.AverageGainDB = newvalue;
        }

        protected void SetAllEqualizerSettings()
        {
            for (int i = 0; i < EqualizerSettings.Bands.Count; i++)
            {
                SetEqualizerValue(EqualizerSettings.Bands[i].Value, i);
            }
        }

        #endregion

        #region Public Methods
        SimpleNotificationSource simpleNotificationSource;
        SingleBlockNotificationStream singleBlockNotificationStream;
        public void OpenTrack(Track track)
        {
            StopPlayback();
            if (CurrentTrack != null) { CurrentTrack.IsPlaying = false; CurrentTrack.Unload(); }
            if (SoundSource != null && !_Crossfade.IsCrossfading) { SoundSource.Dispose(); }
            track.IsPlaying = true;
            Equalizer equalizer;

            SoundSource = CodecFactory.Instance.GetCodec(track.Path);
            if (Settings.SampleRate == -1 && SoundSource.WaveFormat.SampleRate < 44100)
            {
                SoundSource = SoundSource.ChangeSampleRate(44100);
            }
            else if (Settings.SampleRate > -1) { SoundSource.ChangeSampleRate(Settings.SampleRate); }
            SoundSource = SoundSource
.AppendSource(Equalizer.Create10BandEqualizer, out equalizer)
.AppendSource(x => new SingleBlockNotificationStream(x), out singleBlockNotificationStream)
.AppendSource(x => new SimpleNotificationSource(x) { Interval = 100 }, out simpleNotificationSource)
.ToWaveSource(Settings.WaveSourceBits);
            
            MusicEqualizer = equalizer;
            SetAllEqualizerSettings();
            simpleNotificationSource.BlockRead += notifysource_BlockRead;
            singleBlockNotificationStream.SingleBlockRead += notificationSource_SingleBlockRead;

            _analyser = new SampleAnalyser(FFTSize);
            _analyser.Initialize(SoundSource.WaveFormat);
            StopPlayback();
            _soundOut.Initialize(SoundSource);

            CurrentTrack = track;
            OnPropertyChanged("TrackLength");
            OnPropertyChanged("CurrentTrackLength");
            
            CurrentStateChanged();
            _soundOut.Volume = Volume;
            
            if (StartVisualization != null) StartVisualization(this, EventArgs.Empty);
            track.LastTimePlayed = DateTime.Now;
            track.Load();
            if (_Crossfade.IsCrossfading)
                _fader.CrossfadeIn(_soundOut, Volume);
        }

        public void StopPlayback()
        {
            if (_soundOut.PlaybackState == PlaybackState.Playing || _soundOut.PlaybackState == PlaybackState.Paused)
            {
                _manualstop = true;
                _soundOut.Stop();
            }
        }

        public void KickTrack()
        {
            CurrentTrack.Unload();
            CurrentTrack = null;
            SoundSource = null;
            OnPropertyChanged("TrackLength");
            OnPropertyChanged("CurrentTrackLength");
            OnPropertyChanged("Position");
            OnPropertyChanged("CurrentTrackPosition");
            CurrentStateChanged();
        }

        public async void TogglePlayPause()
        {
            if (CurrentTrack == null) return;
            if (_fader != null && _fader.IsFading) { _fader.CancelFading(); _fader.WaitForCancel(); }
            if (_soundOut.PlaybackState == PlaybackState.Playing)
            {
                if (_Crossfade != null && _Crossfade.IsCrossfading) { _Crossfade.CancelFading(); }
                _isfadingout = true;
                await _fader.FadeOut(_soundOut, this.Volume);
                _soundOut.Pause();
                CurrentStateChanged();
                _isfadingout = false;
            }
            else
            {
                _soundOut.Play();
                CurrentStateChanged();
                await _fader.FadeIn(_soundOut, this.Volume);
            }
        }
        #endregion

        #region Protected Methods
        protected void CurrentStateChanged()
        {
            OnPropertyChanged("IsPlaying");
            OnPropertyChanged("CurrentState");
            if (PlaybackStateChanged != null) PlaybackStateChanged(this, new PlayStateChangedEventArgs(this.CurrentState));
        }

        void notificationSource_SingleBlockRead(object sender, SingleBlockReadEventArgs e)
        {
            if (_analyser != null)
                _analyser.Add(e.Left, e.Right);
        }

        void notifysource_BlockRead(object sender, EventArgs e)
        {
            OnPropertyChanged("Position");
            OnPropertyChanged("CurrentTrackPosition");
            var secounds = (int)CurrentTrackPosition.TotalSeconds;
            var totalsecounds = (int)CurrentTrackLength.TotalSeconds;
            if (PositionChanged != null)
                Application.Current.Dispatcher.Invoke(() => PositionChanged(this, new PositionChangedEventArgs(secounds, totalsecounds)));
            if (Settings.IsCrossfadeEnabled && totalsecounds - Settings.CrossfadeDuration > 6 && !_Crossfade.IsCrossfading && totalsecounds - secounds < Settings.CrossfadeDuration)
            {
                _fader.OutDuration = totalsecounds - secounds;
                _Crossfade.FadeOut(Settings.CrossfadeDuration, _soundOut);
                simpleNotificationSource.BlockRead -= notifysource_BlockRead;
                singleBlockNotificationStream.SingleBlockRead -= notificationSource_SingleBlockRead;
                _soundOut.Stopped -= soundOut_Stopped;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    RefreshSoundOut();
                    if (TrackFinished != null) TrackFinished(this, EventArgs.Empty);
                });
            }
        }
        #endregion

        #region Constructor
        public CSCoreEngine()
        {
            _client = new MMNotificationClient();
            RefreshSoundOut();
            _client.DefaultDeviceChanged += client_DefaultDeviceChanged;
            _fader = new VolumeFading();
            _Crossfade = new Crossfade();
        }

        #endregion

        #region SoundOut
        void client_DefaultDeviceChanged(object sender, DefaultDeviceChangedEventArgs e)
        {
            if (Settings.SoundOutDeviceID == "-0" && e.DeviceID != _currentDeviceId)
            {
                _currentDeviceId = e.DeviceID;
                Thread t = new Thread(() =>
                {
                    Thread.Sleep(100);
                    Application.Current.Dispatcher.Invoke(UpdateSoundOut);
                }) { IsBackground = true };
                t.Start();
            }
        }

        public void RefreshSoundOut()
        {
            if (Settings.SoundOutMode == SoundOutMode.DirectSound)
            {
                var enumerator = new DirectSoundDeviceEnumerator();
                DirectSoundDevice device;
                if (Settings.SoundOutDeviceID == "-0")
                {
                    using (var wasapiEnumerator = new MMDeviceEnumerator())
                    {
                        device = enumerator.Devices.FirstOrDefault(x => x.Description == wasapiEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).FriendlyName) ?? enumerator.Devices.First();
                    }
                }
                else
                {
                    device = enumerator.Devices.FirstOrDefault(x => x.Guid.ToString() == Settings.SoundOutDeviceID);
                    if (device == null)
                    {
                        Settings.SoundOutDeviceID = "-0";
                        RefreshSoundOut();
                        return;
                    }
                }
                
                _soundOut = new DirectSoundOut { Device = device.Guid, Latency = Settings.Latency};
            }
            else
            {
                MMDevice device;
                using (var enumerator = new MMDeviceEnumerator())
                {
                    if (Settings.SoundOutDeviceID == "-0")
                    {
                        device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    }
                    else
                    {
                        using (MMDeviceCollection devices = enumerator.EnumAudioEndpoints(DataFlow.Render, DeviceState.Active))
                        {
                            device = devices.First(x => x.DeviceID == Settings.SoundOutDeviceID);

                            if (device == null)
                            {
                                Settings.SoundOutDeviceID = "-0";
                                RefreshSoundOut();
                                return;
                            }
                        }
                    }
                }
                _soundOut = new WasapiOut { Device = device, Latency = Settings.Latency };

            }
            _soundOut.Stopped += soundOut_Stopped;
        }

        public void UpdateSoundOut()
        {
            long position = Position;
            bool isplaying = IsPlaying;
            if (_soundOut != null) { StopPlayback(); _soundOut.Dispose(); }
            RefreshSoundOut();
            if (CurrentTrack != null)
                OpenTrack(CurrentTrack);
            Position = position;
            if (isplaying) TogglePlayPause();
        }

        void soundOut_Stopped(object sender, PlaybackStoppedEventArgs e)
        {
            if (_isdisposing) return;
            if (_manualstop) { _manualstop = false; return; }
            if (TrackFinished != null) TrackFinished(this, EventArgs.Empty);
            CurrentStateChanged();
        }
        #endregion

        #region Visualization Support
        SampleAnalyser _analyser;
        public bool GetFFTData(float[] fftDataBuffer)
        {
            _analyser.CalculateFFT(fftDataBuffer);
            return IsPlaying;
        }

        public int GetFFTFrequencyIndex(int frequency)
        {
            double f;
            if (SoundSource != null)
            {
                f = SoundSource.WaveFormat.SampleRate / 2.0;
            }
            else
            {
                f = 22050; //44100 / 2
            }
            return Convert.ToInt32((frequency / f) * (FFTSize / 2));
        }
        #endregion

        #region IDisposable Support
        protected bool _isdisposing;
        public void Dispose()
        {
            _isdisposing = true;
            if (_soundOut != null)
            {
                if (_fader.IsFading) { _fader.CancelFading(); _fader.WaitForCancel(); }
                _soundOut.Dispose();
                _fader.Dispose();
                _Crossfade.CancelFading();
            }
            if (SoundSource != null) SoundSource.Dispose();
            if (_client != null) _client.Dispose();
        }
        #endregion

        #region Static Methods

        public static List<SoundOutRepresenter> GetSoundOutList()
        {
            List<SoundOutRepresenter> result = new List<SoundOutRepresenter>();
            using (MMDeviceEnumerator enumerator = new MMDeviceEnumerator())
            {
                MMDevice standarddevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (WasapiOut.IsSupportedOnCurrentPlatform)
                {
                    var wasApiItem = new SoundOutRepresenter { Name = "WASAPI", SoundOutMode = SoundOutMode.WASAPI };

                    using (
                        MMDeviceCollection devices = enumerator.EnumAudioEndpoints(DataFlow.Render, DeviceState.Active))
                    {
                        wasApiItem.AudioDevices.Add(new AudioDevice("-0", "Windows Default"));
                        wasApiItem.AudioDevices.AddRange(
                            devices.Select(
                                device =>
                                    new AudioDevice(device.DeviceID, device.FriendlyName,
                                        standarddevice.DeviceID == device.DeviceID)));
                    }
                    result.Add(wasApiItem);
                }

                var directSoundItem = new SoundOutRepresenter { Name = "DirectSound", SoundOutMode = SoundOutMode.DirectSound };
                directSoundItem.AudioDevices.Add(new AudioDevice("-0", "Windows Default"));
                directSoundItem.AudioDevices.AddRange(
                    new DirectSoundDeviceEnumerator().Devices.Select(x => new AudioDevice(x.Guid.ToString(), x.Description, x.Description == standarddevice.FriendlyName)));

                result.Add(directSoundItem);

                return result;
            }
        }
        #endregion
    }
}