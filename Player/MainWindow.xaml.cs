﻿using System;
using System.ComponentModel;
using System.Device;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static System.Configuration.ConfigurationManager;

namespace Player
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            this.Layout();

            if (!LoadStandbyPlayer())
                this.LoadImages();

            this.LoadPlayer();

            this.StartUdpServer();

        }

        private void Layout()
        {
#if !DEBUG
            this.Topmost = true;
            this.Cursor = Cursors.None;
            tbTitle.Visibility = Visibility.Collapsed;
#else
            root.Background = Brushes.Green;
#endif

            var size = new Size(SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
            try
            {
                size = Size.Parse(AppSettings["Size"]);
            }
            catch { }

            this.Width = size.Width;
            this.Height = size.Height;

            var location = new Point(0, 0);
            try
            {
                location = Point.Parse(AppSettings["Location"]);
            }
            catch { }
            this.Left = location.X;
            this.Top = location.Y;

        }

        private bool LoadStandbyPlayer()
        {
            if (File.Exists(standbyVideo))
            {
                root.Children.Remove(imageViewer);
                imageViewer = null;
                standbyPlayer = new MediaElement()
                {
                    Source = new Uri(standbyVideo, UriKind.Absolute),
                    LoadedBehavior = MediaState.Manual,
                    Stretch = Stretch.Fill,
                    Volume = 0
                };
                standbyPlayer.MediaEnded += (s, e) =>
                {
                    standbyPlayer.Position = TimeSpan.Zero;
                };
                Panel.SetZIndex(standbyPlayer, 9999);
                root.Children.Add(standbyPlayer);
                standbyPlayer.Play();
                return true;
            }
            return false;
        }

        private async void LoadImages()
        {
            var images = Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory + "Screen", "*.jpg").ToList();
            images.AddRange(Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory + "Screen", "*.png"));

            if (images.Count > 1)
            {
                imageViewer.ItemsSource = images.OrderBy(f => f.Length)
                                                                    .ThenBy(f => System.IO.Path.GetFileName(f));
                imageViewer.AutoAdvance = true;
                imageViewer.AutoAdvanceDuration = TimeSpan.Parse(AppSettings["ImageDuration"]);
                await Task.Delay(1000);
                imageViewer.SelectedIndex = 0;
            }
            else if (images.Count == 1)
            {
                imageViewer.Background = new ImageBrush(new BitmapImage(new Uri(images[0], UriKind.Absolute)));
            }
        }

        MediaElement standbyPlayer;
        private string standbyVideo = string.IsNullOrEmpty(AppSettings["StandbyVideo"]) ? AppDomain.CurrentDomain.BaseDirectory + "screen.mp4" : AppSettings["StandbyVideo"];
        private string[] videos;
        private int index = 0;
        private bool isPlaying = false;
        UdpClient udpClient;


        private void LoadPlayer()
        {
            var exts = new string[] { ".mp4", ".avi", ".webm", ".wmv", ".mov", ".mkv", ".flv", ".rmvb" };
            var dir = AppDomain.CurrentDomain.BaseDirectory + "Videos";
#if DEBUG
            dir = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
#endif
            videos = Directory.GetFiles(dir)
                                          .Where(f => exts.Contains(System.IO.Path.GetExtension(f)))
                                          .ToArray();

            if (videos.Length < 1)
            {
                MessageBox.Show("未包含任何影片，请添加后重启！");
                Process.Start(dir);
                this.Close();
                return;
            }

            switch (AppSettings["Codec"].ToLower())
            {
                case "ffmpeg":
                    if (Environment.Is64BitProcess)
                        Unosquare.FFME.Library.FFmpegDirectory = AppDomain.CurrentDomain.BaseDirectory + "FFmpeg.4.1.1\\x64";
                    else
                        Unosquare.FFME.Library.FFmpegDirectory = AppDomain.CurrentDomain.BaseDirectory + "FFmpeg.4.1.1\\x86";
                    var player2 = new Unosquare.FFME.MediaElement
                    {
                        Volume = 1,
                        Stretch = Stretch.Fill,
                        LoadedBehavior = Unosquare.FFME.Common.MediaPlaybackState.Manual,
                        Source = new Uri(videos[0], UriKind.Absolute)
                    };
                    player2.BeginInit();
                    root.Children.Insert(1, player2);
                    player2.MediaEnded += OnMediaEnded;
                    player2.MediaOpening += OnMediaOpening;
                    player2.MediaOpened += OnMediaOpened;
                    player2.EndInit();
                    break;
                case "vlc":
                    if (Environment.Is64BitProcess)
                        Meta.Vlc.Wpf.ApiManager.Initialize(@"LibVlc\x64", new string[] { "-I", "dummy", "--ignore-config", "--no-video-title", "--rtsp-tcp" });
                    else
                        Meta.Vlc.Wpf.ApiManager.Initialize(@"LibVlc\x86", new string[] { "-I", "dummy", "--ignore-config", "--no-video-title", "--rtsp-tcp" });
                    var player1 = new Meta.Vlc.Wpf.VlcPlayer
                    {
                        Volume = 1,
                        Stretch = Stretch.Fill,
                    };
                    player1.Initialized += (s, e) =>
                    {
                        player1.LoadMedia(videos[0]);
                    };
                    root.Children.Insert(1, player1);
                    player1.StateChanged += OnStateChanged;
                    player1.LengthChanged += OnLengthChanged;
                    player1.VideoFormatChanging += (s, e) =>
                    {
                        if (e.Width > this.ActualWidth && this.ActualWidth > 0)
                        {
                            var w = this.ActualWidth;
                            var h = w * e.Height / e.Width;
                            e.Width = (uint)w;
                            e.Height = (uint)h;
                        }
                    };
                    break;
                default:
                    var player3 = new MediaElement
                    {
                        Volume = 1,
                        Stretch = Stretch.Fill,
                        LoadedBehavior = MediaState.Manual,
                        Source = new Uri(videos[0], UriKind.Absolute)
                    };
                    player3.MediaEnded += OnMediaEnded;
                    player3.MediaOpened += OnMediaOpened;
                    root.Children.Insert(1, player3);
                    break;
            }

            if (AppSettings["AutoPlay"].ToLower() == "true")
            {
                Task.Delay(200).ContinueWith((t) =>
                {
                    this.Dispatcher.Invoke(() =>
                    {
                        this.Play(1);
                    });
                });

            }
        }

        private void OnMediaOpening(object sender, Unosquare.FFME.Common.MediaOpeningEventArgs e)
        {
            var device = e.Options.VideoStream.HardwareDevices
                                    .FirstOrDefault(d => d.DeviceType == FFmpeg.AutoGen.AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2);
            if (device != null)
                e.Options.VideoHardwareDevice = device;
            else
                e.Options.VideoHardwareDevice = e.Options.VideoStream.HardwareDevices
                                                                             .FirstOrDefault(d => d.DeviceType == FFmpeg.AutoGen.AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA);

            if (e.Options.VideoStream.PixelWidth > this.ActualWidth)
            {
                e.Options.VideoFilter = $"scale={(int)this.ActualWidth}:-1";
            }
        }

        private void OnMediaOpened(object sender, EventArgs e)
        {
            var send = "duration:";
            if (sender is MediaElement player1)
            {
                send += player1.NaturalDuration.TimeSpan.TotalSeconds;
            }
            else if (sender is Unosquare.FFME.MediaElement player2)
            {
                send += player2.NaturalDuration?.TotalSeconds;
                //Debug.WriteLine("video size:{0},{1}", player2.NaturalVideoWidth, player2.NaturalVideoHeight);
            }
            udpClient?.Send(send, IPAddress.Broadcast.ToString(), 10241);

        }

        private void OnLengthChanged(object sender, EventArgs e)
        {
            var player = sender as Meta.Vlc.Wpf.VlcPlayer;
            udpClient?.Send($"duration:{player.Length.TotalSeconds}", IPAddress.Broadcast.ToString(), 10241);
        }

        private async void OnMediaEnded(object sender, EventArgs e)
        {
            CommandManager.Stop(videos[index]);
            if (AppSettings["Loop"] == "true")
            {
                await Task.Delay(200);
                NextVideo();
            }
            else
            {
                this.Stop();
            }
        }

        private async void OnStateChanged(object sender, Meta.Vlc.Event.MediaStateChangedEventArgs e)
        {
            switch (e.State)
            {
                case Meta.Vlc.MediaState.Opening:

                    break;
                case Meta.Vlc.MediaState.Ended:
                    CommandManager.Stop(videos[index]);
                    if (AppSettings["Loop"] == "true")
                    {
                        await Task.Delay(200);
                        NextVideo();
                    }
                    else
                    {
                        this.Stop();
                    }
                    break;
            }
        }

        private async void Play(object name = null)
        {
            if (isPlaying)
                return;

            if (imageViewer != null)
            {
                imageViewer.AutoAdvance = false;
                imageViewer.Visibility = Visibility.Hidden;
            }

            if (standbyPlayer != null)
            {
                standbyPlayer.Stop();
                standbyPlayer.Visibility = Visibility.Hidden;
            }


            var p = "";

            if (name is int n && n > 0 && n <= videos.Length)
            {
                index = n - 1;
                p = videos[index];
            }
            else if (name is string && !string.IsNullOrWhiteSpace(name.ToString()))
            {
                for (int i = 0; i < videos.Length; i++)
                {
                    if (videos[i].EndsWith(name.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        index = i;
                        name = videos[index];
                        break;
                    }
                }
            }

            TimeSpan time = TimeSpan.Zero;
            TimeSpan duration = TimeSpan.Zero;

            if (root.Children[1] is MediaElement player3)
            {
                if (!string.IsNullOrEmpty(p))
                    player3.Source = new Uri(p, UriKind.Absolute);
                player3.Play();
                time = player3.Position;
            }
            else if (root.Children[1] is Unosquare.FFME.MediaElement player2)
            {

                if (!string.IsNullOrEmpty(p))
                    await player2.Open(new Uri(p, UriKind.Absolute));
                //else if (player2.Source == null)
                //    await player2.Open(new Uri(videos[0], UriKind.Absolute));

                await player2.Play();
                time = player2.Position;
            }
            else if (root.Children[1] is Meta.Vlc.Wpf.VlcPlayer player1)
            {

                if (!string.IsNullOrEmpty(p))
                {
                    player1.Stop();
                    player1.LoadMedia(new Uri(p, UriKind.Absolute));
                }
                player1.Play();
                time = player1.Time;
            }

            if (string.IsNullOrEmpty(p))
            {
                p = videos[index];
            }

            if (tbTitle.IsVisible)
            {
                tbTitle.Text = p;
            }

            CommandManager.Start(p, time);

            var cmd = AppSettings["OnPlaying"];
            if (!string.IsNullOrEmpty(cmd))
            {
                foreach (var item in cmd.Split(','))
                {
                    CommandManager.Send(item);
                    await Task.Delay(100).ConfigureAwait(false);
                }
            }

            isPlaying = true;
        }

        private void Pause()
        {
            isPlaying = false;
            if (root.Children[1] is MediaElement player3)
            {
                player3.Pause();
            }
            else if (root.Children[1] is Unosquare.FFME.MediaElement player2)
            {
                player2.Pause();
            }
            else if (root.Children[1] is Meta.Vlc.Wpf.VlcPlayer player1)
            {
                player1.Pause();
            }

            CommandManager.Stop();
        }

        private void Stop()
        {
            isPlaying = false;

            if (standbyPlayer != null)
            {
                standbyPlayer.Position = TimeSpan.Zero;
                standbyPlayer.Play();
                standbyPlayer.Visibility = Visibility.Visible;
            }

            if (imageViewer != null && !imageViewer.IsVisible)
            {
                if (imageViewer.Items.Count > 1)
                {
                    imageViewer.SelectedIndex = 0;
                    imageViewer.AutoAdvance = true;
                }
                imageViewer.Visibility = Visibility.Visible;
            }

            if (root.Children[1] is MediaElement player3)
            {
                player3.Stop();
            }
            else if (root.Children[1] is Unosquare.FFME.MediaElement player2)
            {
                player2.Stop();
            }
            else if (root.Children[1] is Meta.Vlc.Wpf.VlcPlayer player1)
            {
                player1.Stop();
            }

            CommandManager.Stop(videos[index]);

            Task.Delay(100).ContinueWith(t =>
            {
                var cmd = AppSettings["OnStop"];
                if (!string.IsNullOrEmpty(cmd))
                {
                    foreach (var item in cmd.Split(','))
                    {
                        CommandManager.Send(item);
                        Task.Delay(100).Wait();
                    }
                }
            });

        }

        private double Position(TimeSpan? time = null)
        {
            try
            {
                if (root.Children[1] is MediaElement player3)
                {
                    if (time.HasValue)
                    {
                        if (time < TimeSpan.Zero)
                            time = TimeSpan.Zero;
                        else if (time > player3.NaturalDuration.TimeSpan)
                            time = player3.NaturalDuration.TimeSpan;
                        player3.Position = time.Value;
                    }
                    return player3.Position.TotalSeconds;
                }
                else if (root.Children[1] is Unosquare.FFME.MediaElement player2)
                {
                    if (time.HasValue)
                    {
                        if (time < TimeSpan.Zero)
                            time = TimeSpan.Zero;
                        else if (time > player2.NaturalDuration.Value)
                            time = player2.NaturalDuration.Value;
                        player2.Position = time.Value;
                    }
                    return player2.Position.TotalSeconds;
                }
                else if (root.Children[1] is Meta.Vlc.Wpf.VlcPlayer player1)
                {
                    if (time.HasValue)
                    {
                        if (time < TimeSpan.Zero)
                            time = TimeSpan.Zero;
                        else if (time > player1.Length)
                            time = player1.Length;
                        player1.Time = time.Value;
                    }
                    return player1.Time.TotalSeconds;
                }
            }
            catch { }
            return 0;
        }

        private double Duration()
        {
            try
            {
                if (root.Children[1] is MediaElement player3)
                {
                    return player3.NaturalDuration.TimeSpan.TotalSeconds;
                }
                else if (root.Children[1] is Unosquare.FFME.MediaElement player2)
                {
                    return player2.NaturalDuration.Value.TotalSeconds;
                }
                else if (root.Children[1] is Meta.Vlc.Wpf.VlcPlayer player1)
                {
                    return player1.Length.TotalSeconds;
                }
            }
            catch { }
            return 0;
        }

        private void PreviousVideo()
        {
            isPlaying = false;
            index--;
            if (index < 0)
                index = videos.Length - 1;
            this.Play(index + 1);
        }

        private void NextVideo()
        {
            isPlaying = false;
            index++;
            if (index >= videos.Length)
                index = 0;
            this.Play(index + 1);
        }

        private void StartUdpServer()
        {
            var port = 1699;
            try
            {
                port = int.Parse(AppSettings["Port"]);
            }
            catch { }
            udpClient = new UdpClient(port);
            Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        var r = await udpClient.ReceiveAsync();
                        var cmd = Encoding.UTF8.GetString(r.Buffer);
                        var value = "";
                        var send = "";
                        if (cmd.Contains("?"))
                        {
                            var temp = cmd.Split('?');
                            cmd = temp[0];
                            value = temp[1];
                        }
                        switch (cmd)
                        {
                            case "play":
                                this.Dispatcher.Invoke(() =>
                                {
                                    if (string.IsNullOrEmpty(value))
                                    {
                                        this.Play();
                                    }
                                    else if (int.TryParse(value, out int n))
                                    {
                                        isPlaying = false;
                                        this.Play(n);
                                    }
                                    else
                                    {
                                        isPlaying = false;
                                        this.Play(value);
                                    }
                                });
                                break;
                            case "pause":
                                this.Dispatcher.Invoke(this.Pause);
                                break;
                            case "stop":
                                this.Dispatcher.Invoke(this.Stop);
                                break;
                            case "previous":
                                this.Dispatcher.Invoke(this.PreviousVideo);
                                break;
                            case "next":
                                this.Dispatcher.Invoke(this.NextVideo);
                                break;
                            case "volume":
                                send = "";
                                if (int.TryParse(value, out int volume))
                                {
                                    if (volume <= 0)
                                    {
                                        //静音或取消静音
                                        VolumeHelper.Current.IsMute = !VolumeHelper.Current.IsMute;
                                        if (VolumeHelper.Current.IsMute)
                                            send = "volume:0";
                                    }
                                    else if (volume <= 100)
                                    {
                                        VolumeHelper.Current.IsMute = false;
                                        VolumeHelper.Current.MasterVolume = volume;
                                    }
                                }
                                if (string.IsNullOrEmpty(send))
                                    send = "volume:" + VolumeHelper.Current.MasterVolume;
                                break;
                            case "volume-":
                                VolumeHelper.Current.MasterVolume -= 2;
                                send = "volume:" + VolumeHelper.Current.MasterVolume;
                                break;
                            case "volume+":
                                VolumeHelper.Current.MasterVolume += 2;
                                send = "volume:" + VolumeHelper.Current.MasterVolume;
                                break;
                            case "position":
                                if (string.IsNullOrEmpty(value))
                                {
                                    //获取视频播放进度
                                    send = "position:" + this.Dispatcher.Invoke(() => { return this.Position(); });
                                }
                                else if (double.TryParse(value, out double time) && time >= 0)
                                {
                                    send = "position:" + this.Dispatcher.Invoke(() => { return this.Position(TimeSpan.FromSeconds(time)); });
                                }
                                break;
                            case "position-":
                                send = "position:" + this.Dispatcher.Invoke(() =>
                                                                                        {
                                                                                            var p = this.Position() - 3;
                                                                                            return this.Position(TimeSpan.FromSeconds(p));
                                                                                        });
                                break;
                            case "position+":
                                send = "position:" + this.Dispatcher.Invoke(() =>
                                                                                        {
                                                                                            var p = this.Position() + 3;
                                                                                            return this.Position(TimeSpan.FromSeconds(p));
                                                                                        });
                                break;
                            case "duration":
                                send = "duration:" + this.Dispatcher.Invoke(this.Duration);
                                break;
                            case "restart":
                                Process.Start("shutdown", "-r -t 3");
                                break;
                            case "shutdown":
                                Process.Start("shutdown", "-s -t 3");
                                break;
                        }

                        if (!string.IsNullOrEmpty(send))
                            udpClient.Send(send, r.RemoteEndPoint);
                    }
                    catch { break; }
                }
            });
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Left:
                    PreviousVideo();
                    break;
                case Key.Right:
                    NextVideo();
                    break;
                case Key.Enter:
                    this.Play();
                    break;
                case Key.Space:
                    this.Pause();
                    break;
                case Key.Escape:
                    this.Stop();
                    break;
                case Key.Up:
                    VolumeHelper.Current.MasterVolume += 2;
                    break;
                case Key.Down:
                    VolumeHelper.Current.MasterVolume -= 2;
                    break;
            }
            base.OnKeyDown(e);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (root.Children[1] is Meta.Vlc.Wpf.VlcPlayer player1)
            {
                player1.Stop();
                player1.Dispose();
            }
            else if (root.Children[1] is Unosquare.FFME.MediaElement player2)
            {
                player2.Stop();
            }

            base.OnClosing(e);
        }

    }
}
