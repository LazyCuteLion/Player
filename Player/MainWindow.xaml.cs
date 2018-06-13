﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
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

            this.StartUdp();
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
                    root.Children.Add(player3);
                    break;
            }

            if (AppSettings["AutoPlay"].ToLower() == "true")
            {
                this.Play(videos[0]);
            }
        }

        private void OnMediaOpening(object sender, Unosquare.FFME.Events.MediaOpeningRoutedEventArgs e)
        {
            var device = e.Options.VideoStream.HardwareDevices.FirstOrDefault(d => d.DeviceType == AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2);
            if (device != null)
                e.Options.VideoHardwareDevice = device;

            if (e.Options.VideoStream.PixelWidth > this.ActualWidth)
            {
                e.Options.VideoFilter = $"scale={(int)this.ActualWidth}:-1";
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
            if (e.Value == Meta.Vlc.Interop.Media.MediaState.Ended)
            {
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
                }
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

        private void StartUdp()
        {
            var port = 1699;
            try
            {
                port = int.Parse(AppSettings["Port"]);
            }
            catch { }
            var udp = new UdpClient(port);
            Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        var r = await udp.ReceiveAsync();
                        var cmd = System.Text.Encoding.UTF8.GetString(r.Buffer);
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
                                System.Volume.VolumeHelper.Current.IsMute = !System.Volume.VolumeHelper.Current.IsMute;
                                break;
                            case "volume-":
                                System.Volume.VolumeHelper.Current.MasterVolume -= 2;
                                break;
                            case "volume+":
                                System.Volume.VolumeHelper.Current.MasterVolume += 2;
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
                    System.Volume.VolumeHelper.Current.MasterVolume += 2;
                    break;
                case Key.Down:
                    System.Volume.VolumeHelper.Current.MasterVolume -= 2;
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


    }
}
