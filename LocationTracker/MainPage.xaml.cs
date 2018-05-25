﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using uPLibrary.Networking.M2Mqtt;
using Windows.ApplicationModel.Background;
using Windows.ApplicationModel.ExtendedExecution;
using Windows.Devices.Geolocation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Notifications;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.Data.Xml.Dom;
using Newtonsoft.Json;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace LocationTracker
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private MqttClient client;
        private uint _interval;
        ExtendedExecutionSession session = null;
        private Timer periodicTimer = null;
        ApplicationDataContainer localSettings;
        StorageFolder localFolder;

        private long counter;
        private string broker;
        private int port;
        private bool secure;
        private MqttSslProtocols sslprotocol;
        private MqttProtocolVersion protocolversion;
        private string username;
        private string password;

        public enum NotifyType
        {
            StatusMessage,
            ErrorMessage
        };

        //private const string BackgroundTaskName = "LocationTrackerBackgroundTask";
        //private const string BackgroundTaskEntryPoint = "BackgroundTask.LocationBackgroundTask";
        //private IBackgroundTaskRegistration _geolocTask = null;

        public MainPage()
        {
            this.InitializeComponent();

            //put items in the combo boxes
            var protocollist = Enum.GetValues(typeof(MqttProtocolVersion)).Cast<MqttProtocolVersion>().ToList();
            cb_protocol_version.ItemsSource = protocollist;
            cb_protocol_version.SelectedIndex = protocollist.Count - 1;
            cb_ssl_protocol_version.ItemsSource = Enum.GetValues(typeof(MqttSslProtocols)).Cast<MqttSslProtocols>().ToList();
            cb_ssl_protocol_version.SelectedIndex = 0;

            localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
            ReadAllSettings();

            counter = 0;
        }

        protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
        {
            // End the Extended Execution Session.
            // Only one extended execution session can be held by an application at one time.
            ClearExtendedExecution();
        }

        void ClearExtendedExecution()
        {
            if (session != null)
            {
                session.Revoked -= SessionRevoked;
                session.Dispose();
                session = null;
            }

            if (periodicTimer != null)
            {
                periodicTimer.Dispose();
                periodicTimer = null;
            }
        }
        
        private void bt_connect_Click(object sender, RoutedEventArgs e)
        {
            var broker = tb_broker.Text.Trim();
            if (broker.Length == 0)
            {
                //no broker given
                NotifyUser("Enter a hostname for the MQTT broker.",NotifyType.ErrorMessage);
            }
            else
            {
                int port = -1;
                if (!(int.TryParse(tb_port.Text.Trim(), out port)))
                {
                    //invalid port
                    NotifyUser("Enter a valid port number",NotifyType.ErrorMessage);
                }
                else
                {
                    if (cb_protocol_version.SelectedItem == null)
                    {
                        //no protocol version selected
                        NotifyUser("Select a protocol",NotifyType.ErrorMessage);
                    }
                    else
                    {
                        if (cb_ssl_protocol_version.SelectedItem == null)
                        {
                            //no ssl protocol selected
                            NotifyUser("Select a protocol version",NotifyType.ErrorMessage);
                        }
                        else
                        {
                            uint interval = 15;
                            if (!uint.TryParse(tb_interval.Text.Trim(), out interval) || interval <=0)
                            {
                                //invalid interval
                                NotifyUser("Enter a valid interval",NotifyType.ErrorMessage);
                            }
                            else
                            {
                                if(tb_topic.Text.Trim().Length==0)
                                {
                                    //invalid topic
                                    NotifyUser("Enter a valid topic", NotifyType.ErrorMessage);
                                }
                                else
                                {
                                    //parse the input values
                                    var sslprotocol = (MqttSslProtocols)Enum.Parse(typeof(MqttSslProtocols), cb_ssl_protocol_version.SelectedItem.ToString());
                                    var protocolversion = (MqttProtocolVersion)Enum.Parse(typeof(MqttProtocolVersion), cb_protocol_version.SelectedItem.ToString());
                                    this._interval = interval;
                                    //connect
                                    try
                                    {
                                        //store all settings
                                        StoreAllSettings();
                                        this.broker = tb_broker.Text;
                                        this.port = port;
                                        this.secure = cb_secure.IsChecked.Value;
                                        this.sslprotocol = sslprotocol;
                                        this.protocolversion = protocolversion;
                                        this.username = tb_username.Text;
                                        this.password = tb_password.Password;
                                        MqttBroker mqttbroker = new MqttBroker(this.broker,this.port,this.secure,this.sslprotocol,this.protocolversion,this.username,this.password);
                                        mqttbroker.Connect();
                                        
                                        //register extended execution.
                                        BeginExtendedExecution(_interval);
                                    }
                                    catch (Exception ex)
                                    {
                                        NotifyUser(ex.Message+": "+ex.InnerException, NotifyType.ErrorMessage);
                                    }
                                }
                            }
                            
                        }
                    }
                }
            }
        }

        private void StoreAllSettings()
        {
            localSettings.Values["broker"] = tb_broker.Text;
            localSettings.Values["port"] = tb_port.Text;
            localSettings.Values["protocolversion"] = cb_protocol_version.SelectedValue.ToString();
            localSettings.Values["sslprotocol"] = cb_ssl_protocol_version.SelectedValue.ToString();
            localSettings.Values["secure"] = cb_secure.IsChecked.ToString();
            localSettings.Values["username"] = tb_username.Text;
            localSettings.Values["password"] = tb_password.Password;
            localSettings.Values["deviceid"] = tb_deviceid.Text;
            localSettings.Values["interval"] = tb_interval.Text;
            localSettings.Values["topic"] = tb_topic.Text;
        }

        private void ReadAllSettings()
        {
            if(localSettings.Values.Keys.Contains("broker")) tb_broker.Text = localSettings.Values["broker"].ToString();
            if(localSettings.Values.Keys.Contains("port")) tb_port.Text = localSettings.Values["port"].ToString();
            if (localSettings.Values.Keys.Contains("protocolversion")) cb_protocol_version.SelectedItem = localSettings.Values["protocolversion"];
            if (localSettings.Values.Keys.Contains("sslprotocol")) cb_ssl_protocol_version.SelectedItem = localSettings.Values["sslprotocol"];
            if (localSettings.Values.Keys.Contains("secure")) cb_secure.IsChecked = bool.Parse(localSettings.Values["secure"].ToString());
            if (localSettings.Values.Keys.Contains("username")) tb_username.Text = localSettings.Values["username"].ToString();
            if (localSettings.Values.Keys.Contains("password")) tb_password.Password = localSettings.Values["password"].ToString();
            if (localSettings.Values.Keys.Contains("deviceid")) tb_deviceid.Text = localSettings.Values["deviceid"].ToString();
            if (localSettings.Values.Keys.Contains("interval")) tb_interval.Text = localSettings.Values["interval"].ToString();
            if (localSettings.Values.Keys.Contains("topic")) tb_topic.Text = localSettings.Values["topic"].ToString();
        }

        private async void BeginExtendedExecution(uint interval)
        {
            // The previous Extended Execution must be closed before a new one can be requested.
            // This code is redundant here because the sample doesn't allow a new extended
            // execution to begin until the previous one ends, but we leave it here for illustration.
            ClearExtendedExecution();

            var newSession = new ExtendedExecutionSession();
            newSession.Reason = ExtendedExecutionReason.LocationTracking;
            newSession.Description = "Tracking your location";
            newSession.Revoked += SessionRevoked;
            ExtendedExecutionResult result = await newSession.RequestExtensionAsync();

            switch (result)
            {
                case ExtendedExecutionResult.Allowed:
                    this.NotifyUser("Extended execution allowed. Please navigate away from this app.", NotifyType.StatusMessage);
                    session = newSession;
                    Geolocator geolocator = await StartLocationTrackingAsync();
                    periodicTimer = new Timer(OnTimer, geolocator, TimeSpan.FromSeconds(interval), TimeSpan.FromSeconds(interval));
                    break;

                default:
                case ExtendedExecutionResult.Denied:
                    this.NotifyUser("Extended execution denied.", NotifyType.ErrorMessage);
                    newSession.Dispose();
                    break;
            }
            //UpdateUI();
        }

        private async void OnTimer(object state)
        {
            var geolocator = (Geolocator)state;
            string message;
            if (geolocator == null)
            {
                message = "No geolocator";
                DisplayToast(message);
            }
            else
            {
                Geoposition geoposition = await geolocator.GetGeopositionAsync();
                if (geoposition == null)
                {
                    message = "Cannot get current location";
                    DisplayToast(message);
                }
                else
                {
                    Geocoordinate gc = geoposition.Coordinate;
                    BasicGeoposition basicPosition = gc.Point.Position;
                    
                    //message = "{\"_type": "location", "lat": "' + basicPosition.Latitude + '", "lon": "' + basicPosition.Longitude + '", "tst": "' + timestamp + '"';
                    OwntracksLocationMessage locmsg = new OwntracksLocationMessage(basicPosition.Latitude, basicPosition.Longitude, gc.Accuracy);
                    message = JsonConvert.SerializeObject(locmsg);
                    //no toast, just send it to the MQTT broker.
                    //this.client.Publish("home-assistant/jeroen", System.Text.Encoding.UTF8.GetBytes("{'message': 'barf'}"));
                    try
                    {
                        MqttBroker mqttbroker = new MqttBroker(this.broker, this.port, this.secure, this.sslprotocol, this.protocolversion, this.username, this.password);
                        mqttbroker.Connect();

                        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                        {
                        mqttbroker.Publish(tb_topic.Text, System.Text.Encoding.UTF8.GetBytes(message));    
                            
                        });
                        counter++;
                        NotifyUser("Total location messages sent: " + counter.ToString(), NotifyType.StatusMessage);
                    }
                    catch(Exception ex)
                    {
                        NotifyUser(ex.Message, NotifyType.ErrorMessage);
                    }
                }
            }
            
        }

        public static ToastNotification DisplayToast(string content)
        {
            string xml = $@"<toast activationType='foreground'>
                                            <visual>
                                                <binding template='ToastGeneric'>
                                                    <text>Extended Execution</text>
                                                </binding>
                                            </visual>
                                        </toast>";

            XmlDocument doc = new XmlDocument();
            doc.LoadXml(xml);

            var binding = doc.SelectSingleNode("//binding");

            var el = doc.CreateElement("text");
            el.InnerText = content;
            binding.AppendChild(el); //Add content to notification

            var toast = new ToastNotification(doc);

            ToastNotificationManager.CreateToastNotifier().Show(toast); //Show the toast

            return toast;
        }

        private void EndExtendedExecution()
        {
            ClearExtendedExecution();
            //UpdateUI();
        }

        private async void SessionRevoked(object sender, ExtendedExecutionRevokedEventArgs args)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                switch (args.Reason)
                {
                    case ExtendedExecutionRevokedReason.Resumed:
                        this.NotifyUser("Extended execution revoked due to returning to foreground.", NotifyType.StatusMessage);
                        break;

                    case ExtendedExecutionRevokedReason.SystemPolicy:
                        this.NotifyUser("Extended execution revoked due to system policy.", NotifyType.StatusMessage);
                        break;
                }

                EndExtendedExecution();
            });
        }

        /// <summary>
        /// Get permission for location from the user. If the user has already answered once,
        /// this does nothing and the user must manually update their preference via Settings.
        /// </summary>
        private async Task<Geolocator> StartLocationTrackingAsync()
        {
            Geolocator geolocator = null;

            // Request permission to access location
            var accessStatus = await Geolocator.RequestAccessAsync();

            switch (accessStatus)
            {
                case GeolocationAccessStatus.Allowed:
                    geolocator = new Geolocator { ReportInterval = 2000 };
                    break;

                case GeolocationAccessStatus.Denied:
                    this.NotifyUser("Access to location is denied.", NotifyType.ErrorMessage);
                    break;

                case GeolocationAccessStatus.Unspecified:
                    this.NotifyUser("Unspecified error!", NotifyType.ErrorMessage);
                    break;
            }

            return geolocator;
        }

        private async void _geolocTask_Completed(BackgroundTaskRegistration sender, BackgroundTaskCompletedEventArgs e)
        {
            if (sender != null)
            {
                // Update the UI with progress reported by the background task
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        // If the background task threw an exception, display the exception in
                        // the error text box.
                        e.CheckResult();

                        // Update the UI with the completion status of the background task
                        // The Run method of the background task sets this status. 
                        var settings = ApplicationData.Current.LocalSettings;
                        if (settings.Values["Status"] != null)
                        {
                            this.NotifyUser(settings.Values["Status"].ToString(), NotifyType.StatusMessage);
                        }

                        // Extract and display location data set by the background task if not null
                        /*ScenarioOutput_Latitude.Text = (settings.Values["Latitude"] == null) ? "No data" : settings.Values["Latitude"].ToString();
                        ScenarioOutput_Longitude.Text = (settings.Values["Longitude"] == null) ? "No data" : settings.Values["Longitude"].ToString();
                        ScenarioOutput_Accuracy.Text = (settings.Values["Accuracy"] == null) ? "No data" : settings.Values["Accuracy"].ToString();
                        */
                    }
                    catch (Exception ex)
                    {
                        // The background task had an error
                        this.NotifyUser(ex.ToString(), NotifyType.ErrorMessage);
                    }
                });
            }
        }

        // <summary>
        /// Display a message to the user.
        /// This method may be called from any thread.
        /// </summary>
        /// <param name="strMessage"></param>
        /// <param name="type"></param>
        public void NotifyUser(string strMessage, NotifyType type)
        {
            // If called from the UI thread, then update immediately.
            // Otherwise, schedule a task on the UI thread to perform the update.
            if (Dispatcher.HasThreadAccess)
            {
                UpdateStatus(strMessage, type);
            }
            else
            {
                var task = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => UpdateStatus(strMessage, type));
            }
        }

        private void UpdateStatus(string strMessage, NotifyType type)
        {
            switch (type)
            {
                case NotifyType.StatusMessage:
                    StatusBorder.Background = new SolidColorBrush(Windows.UI.Colors.Green);
                    break;
                case NotifyType.ErrorMessage:
                    StatusBorder.Background = new SolidColorBrush(Windows.UI.Colors.Red);
                    break;
            }

            StatusBlock.Text = strMessage;

            // Collapse the StatusBlock if it has no text to conserve real estate.
            StatusBorder.Visibility = (StatusBlock.Text != String.Empty) ? Visibility.Visible : Visibility.Collapsed;
            if (StatusBlock.Text != String.Empty)
            {
                StatusBorder.Visibility = Visibility.Visible;
                StatusPanel.Visibility = Visibility.Visible;
            }
            else
            {
                StatusBorder.Visibility = Visibility.Collapsed;
                StatusPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void tb_username_TextChanged(object sender, TextChangedEventArgs e)
        {
            SetTopicText();
        }

        private void SetTopicText()
        {
            var splits = tb_topic.Text.Split('/');

            tb_topic.Text = splits[0] + "/" + tb_username.Text + "/" + tb_deviceid.Text;
        }

        private void tb_deviceid_TextChanged(object sender, TextChangedEventArgs e)
        {
            SetTopicText();
        }
    }
}
