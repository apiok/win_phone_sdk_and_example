using System;
using System.Windows;
using Microsoft.Phone.Controls;
using Newtonsoft.Json.Linq;
using Odnoklassniki;

namespace PhoneApp1
{
    public partial class MainPage : PhoneApplicationPage
    {
        private const string app_id = "";
        private const string app_public_key = "";
        private const string app_secret_key = "";
        private const string redirect_url = "";
        private const string permissions = "VALUABLE_ACCESS";
        private SDK sdk;

        // Конструктор
        public MainPage()
        {
            InitializeComponent();
            sdk = new SDK(app_id, app_public_key, app_secret_key, redirect_url, permissions);

            // Пример кода для локализации ApplicationBar
            //BuildLocalizedApplicationBar();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (this.sdk.TryLoadSession() == false)
            {
                browser.Visibility = System.Windows.Visibility.Visible;
                this.sdk.Authorize(browser, this, authCallback, errorCallback);
                
            }
            else
            {
                authCallback();
            }
        }

        private void authCallback()
        {
            browser.Visibility = System.Windows.Visibility.Collapsed;
            System.Collections.Generic.Dictionary<string, string> parameters = new System.Collections.Generic.Dictionary<string, string>();
            parameters.Add("fields","first_name,last_name,location,pic_5");
            this.sdk.SendRequest("users.getCurrentUser", parameters, this, getCurrentUserCallback, errorCallback);
        }

        void getCurrentUserCallback(string result)
        {
            JObject resObject = JObject.Parse(result);
            nameField.Text = (string)resObject["first_name"];
            surnameField.Text = (string)resObject["last_name"];
            Utils.downloadImageAsync(new Uri((string)resObject["pic_5"]), this, (i) =>
            {
                userPhotoImage.Source = i;
            }, errorCallback);
            this.sdk.SendRequest("friends.get", null, this, (friendsList) =>
            {
                JArray friendsArray = JArray.Parse(friendsList);
                //System.Diagnostics.Debug.WriteLine(friendsArray[0]);
                System.Collections.Generic.Dictionary<string, string> parameters = new System.Collections.Generic.Dictionary<string, string>();
                parameters.Add("uids", friendsArray[0].ToString());
                parameters.Add("fields", "first_name,last_name");
                this.sdk.SendRequest("users.getInfo", parameters, this, (randomFriend) =>
                {
                    JArray randomFriendObj = JArray.Parse(randomFriend);
                    randomFriendNameLabel.Text = randomFriendObj[0]["first_name"].ToString() + " " + randomFriendObj[0]["last_name"].ToString();
                    randomFriendLabel.Visibility = System.Windows.Visibility.Visible;
                    randomFriendNameLabel.Visibility = System.Windows.Visibility.Visible;
                }, errorCallback);
            }, errorCallback);

        }

        private void errorCallback(Exception e)
        {
            System.Diagnostics.Debug.WriteLine("Exception: " + e.ToString());
            if (e.Message == Odnoklassniki.SDK.ERROR_SESSION_EXPIRED)
            {
                System.Diagnostics.Debug.WriteLine("Session expired error caught. Trying to update session.");
                this.sdk.UpdateToken(this, authCallback, null);
            }
        }


        // Пример кода для построения локализованной панели ApplicationBar
        //private void BuildLocalizedApplicationBar()
        //{
        //    // Установка в качестве ApplicationBar страницы нового экземпляра ApplicationBar.
        //    ApplicationBar = new ApplicationBar();

        //    // Создание новой кнопки и установка текстового значения равным локализованной строке из AppResources.
        //    ApplicationBarIconButton appBarButton = new ApplicationBarIconButton(new Uri("/Assets/AppBar/appbar.add.rest.png", UriKind.Relative));
        //    appBarButton.Text = AppResources.AppBarButtonText;
        //    ApplicationBar.Buttons.Add(appBarButton);

        //    // Создание нового пункта меню с локализованной строкой из AppResources.
        //    ApplicationBarMenuItem appBarMenuItem = new ApplicationBarMenuItem(AppResources.AppBarMenuItemText);
        //    ApplicationBar.MenuItems.Add(appBarMenuItem);
        //}
    }
}