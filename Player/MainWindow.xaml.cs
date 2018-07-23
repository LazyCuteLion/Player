using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using FFmpeg.AutoGen;
using Meta.Vlc.Wpf;
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

            this.LoadImages();

            this.LoadPlayer();

            this.StartUdpServer();

            this.ListeningVolumeChange();
        }

        private void Layout()
        {
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

#if DEBUG
            this.Topmost = false;
            //this.Cursor = Cursors.Arrow;
#endif
        }

        private async void LoadImages()
        {
            var images = Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory + "Screen", "*.jpg").ToList();
            images.AddRange(Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory + "Screen", "*.png"));
            imageViewer.ItemsSource = images.OrderBy(f => f.Length)
                                                                     .ThenBy(f => System.IO.Path.GetFileName(f));

            if (images.Count > 1)
            {
                imageViewer.AutoAdvance = true;
                imageViewer.AutoAdvanceDuration = TimeSpan.Parse(AppSettings["ImageDuration"]);
            }

            await Task.Delay(200);
            imageViewer.SelectedIndex = 0;
        }

        private string[] videos;
        private int index = 0;

        private void LoadPlayer()
        {
            var exts = new string[] { ".mp4", ".avi", ".wmv", ".mov", ".mkv", ".flv", ".rmvb" };
            var dir = AppDomain.CurrentDomain.BaseDirectory + "Videos";
#if DEBUG
            dir = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
#endif
            videos = Directory.GetFiles(dir)
                                          .Where(f => exts.Contains(System.IO.Path.GetExtension(f)))
                                          .ToArray();

            switch (AppSettings["Codec"].ToLower())
            {
                case "vlc":
                    var player1 = new VlcPlayer
                    {
                        Volume = 1,
                        Stretch = Stretch.Fill,
                    };
                    player1.StateChanged += OnStateChanged;
                    player1.LengthChanged += OnLengthChanged;
                    player1.Initialized += (s, e) => { player1.LoadMedia(new Uri(videos[0], UriKind.Absolute)); };
                    root.Children.Add(player1);
                    break;
                case "ffmpeg":
                    Unosquare.FFME.MediaElement.FFmpegDirectory = AppDomain.CurrentDomain.BaseDirectory + "ffmpeg";
                    var player2 = new Unosquare.FFME.MediaElement
                    {
                        Volume = 1,
                        Stretch = Stretch.Fill,
                        LoadedBehavior = MediaState.Play,
                        //Source = new Uri(videos[0], UriKind.Absolute)
                    };
                    root.Children.Add(player2);
                    player2.MediaEnded += OnMediaEnded;
                    player2.MediaOpening += OnMediaOpening;
                    player2.MediaOpened += OnMediaOpened;
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
                    root.Children.Add(player3);
                    break;
            }

            if (AppSettings["AutoPlay"].ToLower() == "true")
            {
                this.Play(videos[0]);
            }
        }

        private void OnMediaOpened(object sender, RoutedEventArgs e)
        {
            var send = "duration:";
            if (sender is MediaElement player1)
            {
                send += player1.NaturalDuration.TimeSpan.TotalSeconds;
            }
            else if (sender is Unosquare.FFME.MediaElement player2)
            {
                send += player2.NaturalDuration.TimeSpan.TotalSeconds;
            }
            else
            {
                send += "0";
            }
            udpClient?.Send(send, IPAddress.Broadcast.ToString(), 10241);
        }

        private void OnLengthChanged(object sender, EventArgs e)
        {
            var player = sender as VlcPlayer;
            udpClient?.Send($"duration:{player.Length.TotalSeconds}", IPAddress.Broadcast.ToString(), 10241);
        }

        private void OnMediaOpening(object sender, Unosquare.FFME.Events.MediaOpeningRoutedEventArgs e)
        {
            var device = e.Options.VideoStream.HardwareDevices.FirstOrDefault(d => d.DeviceType == AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2);
            if (device != null)
                e.Options.VideoHardwareDevice = device;

            if (e.Options.VideoStream.PixelWidth > this.ActualWidth)
            {
                e.Options.VideoFilter = $"scale={(int)this.ActualWidth}x-1";
            }
        }

        private void OnMediaEnded(object sender, RoutedEventArgs e)
        {
            if (AppSettings["Loop"] == "true")
            {
                NextVideo();
            }
            else
            {
                this.Stop();
            }
        }

        private void OnStateChanged(object sender, Meta.Vlc.ObjectEventArgs<Meta.Vlc.Interop.Media.MediaState> e)
        {
            switch (e.Value)
            {
                case Meta.Vlc.Interop.Media.MediaState.Ended:
                    if (AppSettings["Loop"] == "true")
                        NextVideo();
                    else
                    {
                        if (imageViewer.Items.Count > 1)
                        {
                            imageViewer.SelectedIndex = 0;
                            imageViewer.AutoAdvance = true;
                        }
                        imageViewer.Visibility = Visibility.Visible;
                        (sender as VlcPlayer).Stop();

                        this.Send(AppSettings["OnStoped"]);
                    }
                    break;
            }

        }

        private void Play(string name = "")
        {
            imageViewer.AutoAdvance = false;
            imageViewer.Visibility = Visibility.Hidden;

            if (!string.IsNullOrEmpty(name))
            {
                if (int.TryParse(name, out int num))
                {
                    if (num > -1 && num < videos.Length)
                        name = videos[num];
                    else
                        name = "";
                }
                else
                {
                    name = videos.FirstOrDefault(f => f.EndsWith(name));
                }
            }

            if (root.Children[1] is MediaElement player3)
            {
                if (!string.IsNullOrEmpty(name))
                    player3.Source = new Uri(name, UriKind.Absolute);
                player3.Play();
            }
            else if (root.Children[1] is Unosquare.FFME.MediaElement player2)
            {
                if (!string.IsNullOrEmpty(name))
                    player2.Open(new Uri(name, UriKind.Absolute));
                else if (player2.Source == null)
                    player2.Open(new Uri(videos[0], UriKind.Absolute));
                else
                    player2.Play();
            }
            else if (root.Children[1] is VlcPlayer player1)
            {
                if (!string.IsNullOrEmpty(name))
                {
                    player1.Stop();
                    player1.LoadMedia(new Uri(name, UriKind.Absolute));
                }

                player1.Play();
            }

            this.Send(AppSettings["StartPlay"]);
        }

        private void Pause()
        {
            if (root.Children[1] is MediaElement player3)
            {
                player3.Pause();
            }
            else if (root.Children[1] is Unosquare.FFME.MediaElement player2)
            {
                player2.Pause();
            }
            else if (root.Children[1] is VlcPlayer player1)
            {
                player1.Pause();
            }
        }

        private void Stop()
        {
            if (!imageViewer.IsVisible)
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
            else if (root.Children[1] is VlcPlayer player1)
            {
                player1.Stop();
            }

            this.Send(AppSettings["OnStoped"]);

        }

        private double Position(TimeSpan? time = null)
        {
            try
            {
                if (root.Children[1] is MediaElement player3)
                {
                    if (time.HasValue && time < player3.NaturalDuration.TimeSpan)
                        player3.Position = time.Value;
                    return player3.Position.TotalSeconds;
                }
                else if (root.Children[1] is Unosquare.FFME.MediaElement player2)
                {
                    if (time.HasValue && time < player2.NaturalDuration.TimeSpan)
                        player2.Position = time.Value;
                    return player2.Position.TotalSeconds;
                }
                else if (root.Children[1] is VlcPlayer player1)
                {
                    if (time.HasValue && time < player1.Length)
                        player1.Time = time.Value;
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
                    return player2.NaturalDuration.TimeSpan.TotalSeconds;
                }
                else if (root.Children[1] is VlcPlayer player1)
                {
                    return player1.Length.TotalSeconds;
                }
            }
            catch { }
            return 0;
        }

        private void PreviousVideo()
        {
            index--;
            if (index < 0)
                index = videos.Length - 1;
            this.Play(videos[index]);
        }

        private void NextVideo()
        {
            index++;
            if (index >= videos.Length)
                index = 0;
            this.Play(videos[index]);
        }

        UdpClient udpClient;

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
                                    this.Play(value);
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
                                if (string.IsNullOrEmpty(value))
                                {
                                    var volume = System.Device.VolumeHelper.Current.MasterVolume;
                                    udpClient?.Send("volume:" + volume, IPAddress.Broadcast.ToString(), 10241);
                                }
                                else if (int.TryParse(value, out int volume))
                                {
                                    if (volume < 0)
                                    {
                                        //静音或取消静音
                                        System.Device.VolumeHelper.Current.IsMute = !System.Device.VolumeHelper.Current.IsMute;
                                        if (System.Device.VolumeHelper.Current.IsMute)
                                            udpClient?.Send("volume:0", IPAddress.Broadcast.ToString(), 10241);
                                        else
                                            udpClient?.Send("volume:" + System.Device.VolumeHelper.Current.MasterVolume, IPAddress.Broadcast.ToString(), 10241);
                                    }
                                    else
                                    {
                                        System.Device.VolumeHelper.Current.IsMute = true;
                                        System.Device.VolumeHelper.Current.MasterVolume = volume;
                                    }
                                }
                                break;
                            case "volume-":
                                System.Device.VolumeHelper.Current.MasterVolume -= 2;
                                break;
                            case "volume+":
                                System.Device.VolumeHelper.Current.MasterVolume += 2;
                                break;
                            case "position":
                                if (string.IsNullOrEmpty(value))
                                {
                                    //获取视频播放进度
                                    var p = this.Dispatcher.Invoke(() => { return this.Position(); });
                                    udpClient.Send("position:" + p, r.RemoteEndPoint);
                                    udpClient.Send("position:" + p, IPAddress.Broadcast.ToString(), 10241);
                                }
                                else if (double.TryParse(value, out double time) && time >= 0)
                                {
                                    this.Dispatcher.Invoke(() =>
                                    {
                                        this.Position(TimeSpan.FromSeconds(time));
                                    });
                                }
                                break;
                            case "position-":
                                this.Dispatcher.Invoke(() =>
                                {
                                    var p = this.Position() - 3;
                                    if (p < 0)
                                        p = 0;
                                    this.Position(TimeSpan.FromSeconds(p));
                                });
                                break;
                            case "position+":
                                this.Dispatcher.Invoke(() =>
                                {
                                    var p = this.Position() + 3;
                                    if (p > this.Duration())
                                        p = this.Duration();
                                    this.Position(TimeSpan.FromSeconds(p));
                                });
                                break;
                            case "duration":
                                var duration = this.Dispatcher.Invoke(this.Duration);
                                udpClient.Send("duration:" + duration, r.RemoteEndPoint);
                                udpClient.Send("duration:" + duration, IPAddress.Broadcast.ToString(), 10241);
                                break;
                            case "restart":
                                Process.Start("shutdown", "-r -t 3");
                                break;
                            case "shutdown":
                                Process.Start("shutdown", "-s -t 3");
                                break;
                        }
                    }
                    catch { break; }
                }
            });
        }

        private void ListeningVolumeChange()
        {
            System.Device.VolumeHelper.Current.Listening(1000);
            System.Device.VolumeHelper.Current.PropertyChanged += (s, e) =>
            {
                var volume = System.Device.VolumeHelper.Current.MasterVolume;
                udpClient?.Send("volume:" + volume, IPAddress.Broadcast.ToString(), 10241);
            };
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
                    System.Device.VolumeHelper.Current.MasterVolume += 2;
                    break;
                case Key.Down:
                    System.Device.VolumeHelper.Current.MasterVolume -= 2;
                    break;
            }
            base.OnKeyDown(e);
        }

        protected override void OnClosing(CancelEventArgs e)
        {

            if (root.Children[1] is VlcPlayer player1)
            {
                player1.Stop();
                player1.Dispose();
            }
            else if (root.Children[1] is Unosquare.FFME.MediaElement player2)
            {
                player2.Stop();
                player2.Dispose();
            }

            base.OnClosing(e);
        }


        private void Send(string data)
        {
            if (string.IsNullOrWhiteSpace(AppSettings["Com"]) || string.IsNullOrWhiteSpace(data))
                return;

            var temp = data.Split(',');
            Task.Run(() =>
            {
                try
                {
                    var com = new System.IO.Ports.SerialPort(AppSettings["Com"]);
                    com.Open();
                    foreach (var item in temp)
                    {
                        if (item.StartsWith("0x"))
                        {
                            var s = item.Substring(2);
                            var d = new byte[s.Length / 2];
                            for (int i = 0; i < d.Length; i++)
                            {
                                d[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
                            }
                            com.Write(d, 0, d.Length);
                        }
                        else
                        {
                            com.Write(item);
                        }
                        System.Threading.Thread.Sleep(100);
                    }
                    com.Close();
                }
                catch { }
            });

        }
    }
}
