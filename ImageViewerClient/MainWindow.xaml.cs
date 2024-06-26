﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.Data;
using VideoOS.Platform.Messaging;
using VideoOS.Platform.SDK.UI.LoginDialog;
using VideoOS.Platform.UI;

namespace ImageViewerClient
{
    public partial class MainWindow : Window
    {
        private static readonly Guid IntegrationId = new Guid("15B6ACBB-E1B6-4360-86B3-78445C56684D");
        private const string IntegrationName = "ImageViewerClient";
        private const string Version = "1.0";
        private const string ManufacturerName = " Manufacturer";

        private IList<DataType> _streams;
        private FQID _playbackFQID;
        private AudioPlayer _microphonePlayer;
        private AudioPlayer _speakerPlayer;
        private MessageCommunication _mc;
        private Item _selectItem;
        private bool _updatingStreamsFromCode = false;
        private bool _runningOffline = false;

        public MainWindow()
        {
            InitializeComponent();            
        }

        private object IPAddressResponseHandler(VideoOS.Platform.Messaging.Message message, FQID destination, FQID sender)
        {
            string ip = (string)message.Data;
            System.Windows.MessageBox.Show(ip, _selectItem.Name);
            return null;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            SetupControls();
        }

        private void _closeButton_Click(object sender, RoutedEventArgs e)
        {
            VideoOS.Platform.SDK.Environment.RemoveAllServers();
            Close();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            _imageViewerControl.Disconnect();
            _imageViewerControl.Close();
            _imageViewerControl.Dispose();

            if(_microphonePlayer != null)
            {
                _microphonePlayer.Disconnect();
                _microphonePlayer.Close();
            }

            if (_speakerPlayer != null)
            {
                _speakerPlayer.Disconnect();
                _speakerPlayer.Close();
            }


            if (_playbackFQID != null)
            {
                ClientControl.Instance.ReleasePlaybackController(_playbackFQID);
                _playbackFQID = null;
            }
            _mc?.Dispose();
            _dateTimePicker?.Dispose();
            _winFormsHost.Dispose();
        }

        private void liftPrivacyMask_Click(object sender, RoutedEventArgs e)
        {
            Configuration.Instance.ServerFQID.ServerId.UserContext.SetPrivacyMaskLifted(!Configuration.Instance.ServerFQID.ServerId.UserContext.PrivacyMaskLifted);
        }

        private void SetupControls()
        {
            _imageViewerControl.Disconnect();

            _imageViewerControl.EnableDigitalZoom = _digitalZoomCheckBox.IsChecked.Value;
            _imageViewerControl.MaintainImageAspectRatio = _maintainAspectRatioCheckBox.IsChecked.Value;
            _imageViewerControl.EnableVisibleHeader = _visibleHeaderCheckBox.IsChecked.Value;
            _imageViewerControl.EnableVisibleCameraName = _visibleCameraNameCheckBox.IsChecked.Value;
            _imageViewerControl.EnableVisibleLiveIndicator = _visibleLiveIndicatorCheckBox.IsChecked.Value;
            _imageViewerControl.EnableVisibleTimestamp = _visibleTimeStampCheckBox.IsChecked.Value;

            if (_playbackFQID == null)
            {
                _playbackFQID = ClientControl.Instance.GeneratePlaybackController();
                _playbackUserControl.Init(_playbackFQID);
                SetPlaybackSkipMode();
            }
        }

        private void _selectCameraButton_Click(object sender, RoutedEventArgs e)
        {
            var secondWindow = new ItemPickerWpfWindow()
            {
                Items = Configuration.Instance.GetItems(),
                KindsFilter = new List<Guid>() { Kind.Camera }
            };
            if (secondWindow.ShowDialog().Value)
            {
                var items = secondWindow.SelectedItems;
                if (items != null && items.Any())
                {
                    SetupControls();
                    _selectItem = items.First();
                    LoadCamera(_selectItem);
                    var relatedItems = _selectItem.GetRelated();
                    LoadMicrophone(relatedItems);
                    LoadSpeaker(relatedItems);
                    _playbackUserControl.SetCameras(new List<FQID>() { _selectItem.FQID });
                }
            }
        }

        private void LoadCamera(Item selectedItem)
        {
            _updatingStreamsFromCode = true;
            var streamDataSource = new StreamDataSource(selectedItem);
            _streams = streamDataSource.GetTypes();
            _streamsComboBox.ItemsSource = _streams;
            foreach (DataType stream in _streamsComboBox.Items)
            {
                if (stream.Properties.ContainsKey("Default"))
                {
                    if (stream.Properties["Default"] == "Yes")
                    {
                        _streamsComboBox.SelectedItem = stream;
                    }
                }
            }
            _updatingStreamsFromCode = false;

            _selectCameraButton.Content = selectedItem.Name;
            
            _imageViewerControl.CameraFQID = selectedItem.FQID;
            _imageViewerControl.PlaybackControllerFQID = _playbackFQID;
            if (_streamsComboBox.SelectedItem != null)
            {
                _imageViewerControl.StreamId = ((DataType)_streamsComboBox.SelectedItem).Id;
            }
            _imageViewerControl.Initialize();
            _imageViewerControl.Connect();

            _imageViewerControl.Selected = true;
            EnvironmentManager.Instance.Mode = Mode.ClientPlayback;

            if (!_runningOffline)
            {
                _playbackRadioButton.IsEnabled = true;
                _playbackRadioButton.IsChecked = true;
                _liveRadioButton.IsEnabled = true;
            }else
            {
                _playbackRadioButton.IsChecked = true;
                _playbackRadioButton.IsEnabled = false;
                _liveRadioButton.IsEnabled = false;
            }
            EnablePlayback();
        }

        private void LoadMicrophone(IEnumerable<Item> relatedItems)
        {
            var item = relatedItems.FirstOrDefault(x => x.FQID.Kind == Kind.Microphone);
            if(item != null)
            {
                _microphonePlayer = GenerateAudioplayer();
                _microphonePlayer.MicrophoneFQID = item.FQID;
                _microphonePlayer.Connect();
            }
        }

        private void LoadSpeaker(IEnumerable<Item> relatedItems)
        {
            var item = relatedItems.FirstOrDefault(x => x.FQID.Kind == Kind.Speaker);
            if (item != null)
            {
                _speakerPlayer = GenerateAudioplayer();
                _speakerPlayer.SpeakerFQID = item.FQID;
                _speakerPlayer.Connect();
            }
        }

        private AudioPlayer GenerateAudioplayer()
        {
            AudioPlayer audioPlayer = new AudioPlayer();
            audioPlayer.Initialize();
            audioPlayer.PlaybackControllerFQID = _playbackFQID;
            return audioPlayer;
        }

        private void EnablePlayback()
        {
            if (_playbackRadioButton.IsChecked.Value)
            {
                _streamsComboBox.IsEnabled = false;
                _visibleTimeStampCheckBox.IsEnabled = true;
                _adaptiveStreamingCheckBox.IsEnabled = false;
                _playbackUserControl.Visibility = Visibility.Visible;
                _playbackUserControl.SetEnabled(true);
                EnvironmentManager.Instance.SendMessage(new VideoOS.Platform.Messaging.Message(
                                                            MessageId.System.ModeChangeCommand,
                                                            Mode.ClientPlayback), _playbackFQID);
                _playbackCommandsStackPanel.Visibility = Visibility.Visible;
            }
        }

        private void _streamsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_updatingStreamsFromCode &&  _imageViewerControl != null && _imageViewerControl.CameraFQID != null && _streamsComboBox.SelectedItem != null)
            {
                    _imageViewerControl.Disconnect();
                     DataType selectStream = (DataType)_streamsComboBox.SelectedItem;

                    _imageViewerControl.StreamId = selectStream.Id;
                    _imageViewerControl.Connect();
            }
        } 
        private void _liveRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            _playbackUserControl.SetEnabled(false);
            _playbackUserControl.Visibility = Visibility.Hidden;
            _visibleTimeStampCheckBox.IsEnabled = false;
            _adaptiveStreamingCheckBox.IsEnabled = true;
            EnvironmentManager.Instance.SendMessage(new VideoOS.Platform.Messaging.Message(
                                                        MessageId.System.ModeChangeCommand,
                                                        Mode.ClientLive), _playbackFQID);
            if (_streams.Count > 1)
            {
                _streamsComboBox.IsEnabled = true;
            }
            _playbackCommandsStackPanel.Visibility = Visibility.Hidden;
        }

        private void _playbackRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            EnablePlayback();
        }

        private void _digitalZoomCheckBox_Click(object sender, RoutedEventArgs e)
        {
            _imageViewerControl.EnableDigitalZoom = _digitalZoomCheckBox.IsChecked.Value;
        }

        private void _maintainAspectRatioCheckBox_Click(object sender, RoutedEventArgs e)
        {
            _imageViewerControl.MaintainImageAspectRatio = _maintainAspectRatioCheckBox.IsChecked.Value;
        }

        private void _visibleHeaderCheckBox_Click(object sender, RoutedEventArgs e)
        {
            _imageViewerControl.EnableVisibleHeader = _visibleHeaderCheckBox.IsChecked.Value;
        }

        private void _visibleCameraNameCheckBox_Click(object sender, RoutedEventArgs e)
        {
            _imageViewerControl.EnableVisibleCameraName = _visibleCameraNameCheckBox.IsChecked.Value;
        }

        private void _visibleLiveIndicatorCheckBox_Click(object sender, RoutedEventArgs e)
        {
            _imageViewerControl.EnableVisibleLiveIndicator = _visibleLiveIndicatorCheckBox.IsChecked.Value;
        }

        private void _visibleTimeStampCheckBox_Click(object sender, RoutedEventArgs e)
        {
            _imageViewerControl.EnableVisibleTimestamp = _visibleTimeStampCheckBox.IsChecked.Value;
        }

        private void _adaptiveStreamingCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (_updatingStreamsFromCode || _imageViewerControl == null || _imageViewerControl.CameraFQID == null)
            {
                return;
            }

            if (_adaptiveStreamingCheckBox.IsChecked == true)
            {
                _streamsComboBox.IsEnabled = false;
                _imageViewerControl.StreamId = Guid.Empty;
                _imageViewerControl.AdaptiveStreaming = true;
                _imageViewerControl.Connect();
            }
            else
            {
                _streamsComboBox.IsEnabled = true;
                DataType selectStream = (DataType)_streamsComboBox.SelectedItem;
                _imageViewerControl.StreamId = selectStream.Id;
                _imageViewerControl.AdaptiveStreaming = false;
                _imageViewerControl.Connect();
            }
        }


        private void _diagnosticsCheckBox_Click(object sender, RoutedEventArgs e)
        {
            EnvironmentManager.Instance.EnvironmentOptions["PlayerDiagnosticLevel"] = _diagnosticsCheckBox.IsChecked.Value ? "3" : "0";
            EnvironmentManager.Instance.FireEnvironmentOptionsChangedEvent();
        }

        private void _checkAllRadioButtonsChecked(object sender, RoutedEventArgs e)
        {
            SetPlaybackSkipMode();
        }

        private void _showTallCheckBox_Click(object sender, RoutedEventArgs e)
        {
            _playbackUserControl.ShowTallUserControl = _showTallCheckBox.IsChecked.Value;
        }

        private void _showSpeedCheckBox_Click(object sender, RoutedEventArgs e)
        {
            _playbackUserControl.ShowSpeedControl = _showSpeedCheckBox.IsChecked.Value;
        }

        private void _showTimeSpanCheckBox_Click(object sender, RoutedEventArgs e)
        {
            _playbackUserControl.ShowTimeSpanControl = _showTimeSpanCheckBox.IsChecked.Value;
        }

        private void _stopButton_Click(object sender, RoutedEventArgs e)
        {
            EnvironmentManager.Instance.SendMessage(new VideoOS.Platform.Messaging.Message(
                                            MessageId.SmartClient.PlaybackCommand,
                                            new PlaybackCommandData() { Command = PlaybackData.PlayStop }), _playbackFQID);
        }

        private void _forwardButton_Click(object sender, RoutedEventArgs e)
        {
            EnvironmentManager.Instance.SendMessage(new VideoOS.Platform.Messaging.Message(
                                            MessageId.SmartClient.PlaybackCommand,
                                            new PlaybackCommandData() { Command = PlaybackData.PlayForward, Speed = 1.0 }), _playbackFQID);

        }

        private void _dateTimePicker_ValueChanged(object sender, EventArgs e)
        {
            EnvironmentManager.Instance.SendMessage(new VideoOS.Platform.Messaging.Message(
                                                        MessageId.SmartClient.PlaybackCommand,
                                                        new PlaybackCommandData() { Command = PlaybackData.PlayStop }), _playbackFQID);
            EnvironmentManager.Instance.SendMessage(new VideoOS.Platform.Messaging.Message(
                                                        MessageId.SmartClient.PlaybackCommand,
                                                        new PlaybackCommandData() { Command = PlaybackData.Goto, DateTime = _dateTimePicker.Value.ToUniversalTime() }), _playbackFQID);
        }

        private void _maxForwardSpeedButton_Click(object sender, RoutedEventArgs e)
        {
            if (_playbackFQID != null)
            {
                PlaybackCommandData data = new PlaybackCommandData() { Command = PlaybackData.PlayForward, Speed = 32 };
                EnvironmentManager.Instance.SendMessage(new VideoOS.Platform.Messaging.Message(
                                                    MessageId.SmartClient.PlaybackCommand,
                                                    data), _playbackFQID);
            }
        }

        private void _maxTimespanButton_Click(object sender, RoutedEventArgs e)
        {
            if (_playbackUserControl != null && _playbackUserControl.TimeSpan.Days != 28)
            {
                _playbackUserControl.TimeSpan = new TimeSpan(28, 0, 0, 0);
            }
        }

        private void _IpButton_Click(object sender, RoutedEventArgs e)
        {
            EnsureMessageCommunicationInitialized();
            _mc.TransmitMessage(new VideoOS.Platform.Messaging.Message(MessageId.Server.GetIPAddressRequest, _selectItem.FQID), null, null, null);
        }

        private void EnsureMessageCommunicationInitialized()
        {
            if (_mc == null)
            {
                MessageCommunicationManager.Start(EnvironmentManager.Instance.MasterSite.ServerId);
                _mc = MessageCommunicationManager.Get(EnvironmentManager.Instance.MasterSite.ServerId);
                _mc.RegisterCommunicationFilter(IPAddressResponseHandler, new CommunicationIdFilter(MessageId.Server.GetIPAddressResponse));
            }
        }

        private void SetPlaybackSkipMode()
        {
            if (_skipRadioButton.IsChecked.Value)
            {
                EnvironmentManager.Instance.SendMessage(new VideoOS.Platform.Messaging.Message(
                                                                MessageId.SmartClient.PlaybackSkipModeCommand,
                                                                PlaybackSkipModeData.Skip), _playbackFQID);
            }
            else if (_noSkipRadioButton.IsChecked.Value)
            {
                EnvironmentManager.Instance.SendMessage(new VideoOS.Platform.Messaging.Message(
                                                                             MessageId.SmartClient.PlaybackSkipModeCommand,
                                                                             PlaybackSkipModeData.Noskip), _playbackFQID);
            }
            else if (_stopRadioButton.IsChecked.Value)
            {
                EnvironmentManager.Instance.SendMessage(new VideoOS.Platform.Messaging.Message(
                                                                             MessageId.SmartClient.PlaybackSkipModeCommand,
                                                                             PlaybackSkipModeData.StopAtSequenceEnd), _playbackFQID);
            }
        }

        private void showSelectCameraButton()
        {
            _loginButton.Visibility = Visibility.Collapsed;
            _offlineScpButton.Visibility = Visibility.Collapsed;
            _selectCameraButton.Visibility = Visibility.Visible;
        }

        private void _loginButton_Click(object sender, RoutedEventArgs e)
        {
            var loginWindow = new DialogLoginForm(SetLoginResult, IntegrationId, IntegrationName, Version, ManufacturerName);
            loginWindow.ShowDialog();
            if (Connected)
            {
                _runningOffline = false;
                loginWindow.Close();
                showSelectCameraButton();
                SetupControls();
            }
        }

        private static bool Connected = false;
        private static void SetLoginResult(bool connected)
        {
            Connected = connected;
        }

        private void _offlineScpButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog();
            if (openFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string path = openFileDialog.FileName;

                Uri fileUri = new Uri(path);
                string password = "";
                while (true)
                {
                    VideoOS.Platform.SDK.Environment.RemoveAllServers();
                    VideoOS.Platform.SDK.Environment.AddServer(false, fileUri, new System.Net.NetworkCredential("", password));
                    try
                    {
                        VideoOS.Platform.SDK.Environment.Login(fileUri, IntegrationId, IntegrationName, Version, ManufacturerName);
                        VideoOS.Platform.SDK.Environment.LoadConfiguration(fileUri);

                        showSelectCameraButton();
                        _runningOffline = true;
                        break;
                    }
                    catch (NotAuthorizedMIPException)
                    {
                        PasswordWindow window = new PasswordWindow();
                        if ((bool)window.ShowDialog())
                        {
                            password = window.Password;
                        }
                        else
                        {
                            return;
                        }
                    }
                }
            }
        }        
    }
}
