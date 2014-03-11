using System;
using System.Net;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Phone.Controls;
using Newtonsoft.Json.Linq;
using Odnoklassniki;

// ReSharper disable RedundantThisQualifier

namespace PhoneApp1
{
    // Only classes inherited from PhoneApplicationPage can be passed as callbackContext to SDK, because yhey have Dispatcher
    // ReSharper disable RedundantExtendsListEntry
    public partial class MainPage : PhoneApplicationPage
    // ReSharper restore RedundantExtendsListEntry
    {
        private const string AppId = "";
        private const string AppPublicKey = "";
        private const string AppSecretKey = "";
        private const string RedirectUrl = "";
        private const string Permissions = "VALUABLE_ACCESS";
        private readonly SDK _sdk;

        public MainPage()
        {
            this.InitializeComponent();
            this._sdk = new SDK(AppId, AppPublicKey, AppSecretKey, RedirectUrl, Permissions);
        }

        // tries to login with existing session if it exists and initiates authorization if not
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (this._sdk.TryLoadSession() == false)
            {
                this.Browser.Visibility = Visibility.Visible;
                this._sdk.Authorize(Browser, this, AuthCallback, ErrorCallback);
                
            }
            else
            {
                this.AuthCallback();
            }
        }

        // uses users.getCurrentUser to get info about authorized user: first name, last name, location and photo 128*128 url
        private void AuthCallback()
        {
            this.Browser.Visibility = Visibility.Collapsed;
            System.Collections.Generic.Dictionary<string, string> parameters = new System.Collections.Generic.Dictionary<string, string>
            {
                {"fields", "first_name,last_name,location,pic_5"}
            };
            this._sdk.SendRequest("users.getCurrentUser", parameters, this, GetCurrentUserCallback, ErrorCallback);
        }

        // downloads and sets user photo and one friend's name
        void GetCurrentUserCallback(string result)
        {
            JObject resObject = JObject.Parse(result);
            this.NameField.Text = (string)resObject["first_name"];
            this.SurnameField.Text = (string)resObject["last_name"];
            Utils.DownloadImageAsync(new Uri((string)resObject["pic_5"]), this, i =>
            {
                this.UserPhotoImage.Source = i;
            }, ErrorCallback);
            this._sdk.SendRequest("friends.get", null, this, friendsList =>
            {
                JArray friendsArray = JArray.Parse(friendsList);
                System.Collections.Generic.Dictionary<string, string> parameters = new System.Collections.Generic.Dictionary<string, string>
                {
                    {"uids", friendsArray[0].ToString()},
                    {"fields", "first_name,last_name"}
                };
                this._sdk.SendRequest("users.getInfo", parameters, this, randomFriend =>
                {
                    JArray randomFriendObj = JArray.Parse(randomFriend);
                    this.RandomFriendNameLabel.Text = randomFriendObj[0]["first_name"].ToString() + " " + randomFriendObj[0]["last_name"].ToString();
                    this.RandomFriendLabel.Visibility = Visibility.Visible;
                    this.RandomFriendNameLabel.Visibility = Visibility.Visible;
                }, ErrorCallback);
            }, ErrorCallback);

        }

        // prints debug error message to stdout
        private void ErrorCallback(Exception e)
        {
            System.Diagnostics.Debug.WriteLine("Exception: " + e);
            if (e.Message != SDK.ErrorSessionExpired) return;
            System.Diagnostics.Debug.WriteLine("Session expired error caught. Trying to update session.");
            this._sdk.UpdateToken(this, AuthCallback, null);
        }
    }

    class Utils
    {
        public static void DownloadImageAsync(Uri imageAbsoluteUri, PhoneApplicationPage context, Action<BitmapImage> callbackOnSuccess, Action<Exception> callbackOnError)
        {
            try
            {
                WebClient wc = new WebClient();
                wc.OpenReadCompleted += (s, e) =>
                {
                    if (e.Error == null && !e.Cancelled)
                    {
                        try
                        {
                            BitmapImage image = new BitmapImage();
                            image.SetSource(e.Result);
                            context.Dispatcher.BeginInvoke(() => callbackOnSuccess.Invoke(image));
                        }
                        catch (Exception ex)
                        {
                            if (callbackOnError != null)
                            {
                                callbackOnError.Invoke(ex);
                            }
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Error downloading image");
                    }
                };
                wc.OpenReadAsync(imageAbsoluteUri, wc);
            }
            catch (Exception e)
            {
                if (callbackOnError != null)
                {
                    callbackOnError.Invoke(e);
                }
            }
        }
    }
}