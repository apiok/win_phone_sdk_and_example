﻿using System;
using System.IO;
using System.Net;
using System.Collections.Generic;
using System.Text;
using Microsoft.Phone.Controls;
using System.Windows.Navigation;
using System.Windows.Media.Imaging;
using System.IO.IsolatedStorage;
using Odnoklassniki.ServiceStructures;
using System.ComponentModel;
using System.Linq;

// ReSharper disable RedundantThisQualifier

// ReSharper disable once CheckNamespace
namespace Odnoklassniki
{
// ReSharper disable once InconsistentNaming
    class SDK
    {

        [DefaultValue("OK_SDK_")]
        public static string SettingsPrefix{get; set;}

        /*
         * Uris, uri templates, data templates
         */
        private const string UriApiRequest = "http://api.odnoklassniki.ru/fb.do";
        private const string UriTokenRequest = "http://api.odnoklassniki.ru/oauth/token.do";
        private const string UriTemplateAuth = "http://www.odnoklassniki.ru/oauth/authorize?client_id={0}&scope={1}&response_type=code&redirect_uri={2}&layout=m";
        private const string DataTemplateAuthTokenRequest = "code={0}&redirect_uri={1}&grant_type=authorization_code&client_id={2}&client_secret={3}";
        private const string DataTemplateAuthTokenUpdateRequest = "refresh_token={0}&grant_type=refresh_token&client_id={1}&client_secret={2}";
        /*
         * End uris, uri templates, data templates
         */
        public const string ErrorSessionExpired = "SESSION_EXPIRED";
        public const string ErrorNoTokenSentByServer = "NO_ACCESS_TOKEN_SENT_BY_SERVER";
        /*
         * if you see this text, read about errors here http://apiok.ru/wiki/pages/viewpage.action?pageId=77824003
         */
        public const string ErrorBadApiRequest = "BAD_API_REQUEST";
        private const string SdkException = "Odnoklassniki sdk exception. Please, check your app info, request correctness and internet connection. If problem persists, contact SDK developers with error and your actions description.";
        private const string ParameterNameAccessToken = "access_token";
        private const string ParameterNameRefreshToken = "refresh_token";
        private readonly string _appId;
        private readonly string _appPublicKey;
        private readonly string _appSecretKey;
        private readonly string _redirectUrl;
        private readonly string _permissions;
        private string _accessToken;
        private string _refreshToken;
        private string _code;
        private readonly ConcurrentDictionary<HttpWebRequest, CallbackStruct> _callbacks = new ConcurrentDictionary<HttpWebRequest, CallbackStruct>();
        private AuthCallbackStruct _authCallback, _updateCallback;

        private enum OAuthRequestType : byte { OAuthTypeAuth, OAuthTypeUpdateToken };

        public SDK(string applicationId, string applicationPublicKey, string applicationSecretKey, string redirectUrl, string permissions)
        {

            this._appId = applicationId;
            this._appPublicKey = applicationPublicKey;
            this._appSecretKey = applicationSecretKey;
            this._redirectUrl = redirectUrl;
            this._permissions = permissions;
        }

        /**
         * Authorize the application with permissions.
         * Calls onSuccess after correct response, onError otherwise(in callbackContext thread).
         * @param browser - browser element will be used for OAuth2 authorisation.
         * @param callbackContext - PhoneApplicationPage in context of witch RequestCallback would be called. Used to make working with UI components from callbacks simplier.
         * @param onSuccess - this function will be called after success authorisation(in callbackContext thread)
         * @param onError - this function will be called after unsuccess authorisation(in callbackContext thread)
         */
        public void Authorize(WebBrowser browser, PhoneApplicationPage callbackContext, Action onSuccess, Action<Exception> onError, bool saveSession = true)
        {
            this._authCallback.OnSuccess = onSuccess;
            this._authCallback.OnError = onError;
            this._authCallback.CallbackContext = callbackContext;
            this._authCallback.SaveSession = saveSession;
            Uri uri = new Uri(String.Format(UriTemplateAuth, this._appId, this._permissions, this._redirectUrl), UriKind.Absolute);
            browser.Navigated += NavigateHandler;
            browser.Navigate(uri);
        }

        /*
         * Prepairs and sends API request.
         * Calls onSuccess after correct response, onError otherwise(in callbackContext thread).
         * @param method methodname
         * @param parameters dictionary "parameter_name":"parameter_value"
         * @param callbackContext - PhoneApplicationPage in context of witch RequestCallback would be called. Used to make working with UI components from callbacks simplier.
         * @param onSuccess - this function will be called after success authorisation(in callbackContext thread)
         * @param onError - this function will be called after unsuccess authorisation(in callbackContext thread)
         */
        public void SendRequest(string method, Dictionary<string, string> parameters, PhoneApplicationPage callbackContext, Action<string> onSuccess, Action<Exception> onError)
        {
            try
            {
                Dictionary<string, string> parametersLocal = parameters == null ? new Dictionary<string, string>() : new Dictionary<string, string>(parameters);
                StringBuilder builder = new StringBuilder(UriApiRequest).Append("?");
                parametersLocal.Add("sig", this.CalcSignature(method, parameters));
                parametersLocal.Add("application_key", this._appPublicKey);
                parametersLocal.Add("method", method);
                parametersLocal.Add(ParameterNameAccessToken, this._accessToken);
                foreach (KeyValuePair<string, string> pair in parametersLocal)
                {
                    builder.Append(pair.Key).Append("=").Append(pair.Value).Append("&");
                }
                // removing last & added with cycle
                builder.Remove(builder.Length - 1, 1);
                HttpWebRequest request = HttpWebRequest.CreateHttp(builder.ToString());
                CallbackStruct callbackStruct;
                callbackStruct.OnSuccess = onSuccess;
                callbackStruct.CallbackContext = callbackContext;
                callbackStruct.OnError = onError;
                this._callbacks.SafeAdd(request, callbackStruct);
                request.BeginGetResponse(this.RequestCallback, request);
            }
            catch (Exception e)
            {
                if (onError != null)
                {
                    onError.Invoke(new Exception(SdkException, e));
                }
            }
        }

        /**
         * Tries to update access_token with refresh_token.
         * Calls onSuccess after correct response, onError otherwise(in callbackContext thread).
         * @param callbackContext - PhoneApplicationPage in context of witch RequestCallback would be called. Used to make working with UI components from callbacks simplier.
         * @param onSuccess - this function will be called after success authorisation(in callbackContext thread)
         * @param onError - this function will be called after unsuccess authorisation(in callbackContext thread)
         */
        public void UpdateToken(PhoneApplicationPage callbackContext, Action onSuccess, Action<Exception> onError, bool saveSession = true)
        {
            this._updateCallback.CallbackContext = callbackContext;
            this._updateCallback.OnSuccess = onSuccess;
            this._updateCallback.SaveSession = saveSession;
            this._updateCallback.OnError = onError;
            try
            {
                BeginOAuthRequest(SDK.OAuthRequestType.OAuthTypeUpdateToken);
            }
            catch(Exception e)
            {
                if (onError != null)
                {
                    onError.Invoke(new Exception(SdkException, e));
                }
            }
        }
 
        /**
         * Saves acces_token and refresh_token to application isolated storage.
         */
        public void SaveSession()
        {
            try
            {
                IsolatedStorageSettings appSettings = IsolatedStorageSettings.ApplicationSettings;
                appSettings[SDK.SettingsPrefix + SDK.ParameterNameAccessToken] = this._accessToken;
                appSettings[SDK.SettingsPrefix + SDK.ParameterNameRefreshToken] = this._refreshToken;
                appSettings.Save();
            }
            catch (IsolatedStorageException e)
            {
                throw new Exception(SdkException, e);
            }
        }

        /**
         * Tries to load acces_token and refresh_token from application isolated storage.
         * This function doesn't guarantee, that tokens are correct.
         * @return true if access_tokent and refresh_token loaded from isolated storage false otherwise
         */
        public bool TryLoadSession()
        {
            IsolatedStorageSettings appSettings = IsolatedStorageSettings.ApplicationSettings;
            if (appSettings.Contains(SDK.SettingsPrefix + SDK.ParameterNameAccessToken) && appSettings.Contains(SDK.SettingsPrefix + SDK.ParameterNameRefreshToken))
            {
                this._accessToken = (string)appSettings[SDK.SettingsPrefix + SDK.ParameterNameAccessToken];
                this._refreshToken = (string)appSettings[SDK.SettingsPrefix + SDK.ParameterNameRefreshToken];
                return this._accessToken != null && this._refreshToken != null;
            }
            return false;
        }

        /*
         * Removes access_token and refresh_token from appliction isolated storage and object.
         * You have to get new tokens usin Authorise method after calling this method.
         */
        public void ResetSession()
        {
            this._accessToken = null;
            this._refreshToken = null;
            IsolatedStorageSettings appSettings = IsolatedStorageSettings.ApplicationSettings;
            appSettings.Remove(SDK.SettingsPrefix + SDK.ParameterNameAccessToken);
            appSettings.Remove(SDK.SettingsPrefix + SDK.ParameterNameRefreshToken);
        }

        #region functions used for authorisation and updating token

        private void NavigateHandler(object sender, NavigationEventArgs e)
        {
            try
            {
                string query = e.Uri.Query;
                if (query.IndexOf("code=") != -1)
                {
                    this._code = query.Substring(query.IndexOf("code=") + 5);
                    this.BeginOAuthRequest(SDK.OAuthRequestType.OAuthTypeAuth);
                }
                else if (query.IndexOf("error=") != -1)
                {
                    throw new Exception(query.Substring(query.IndexOf("error=") + 6));
                }
            }
            catch (Exception ex)
            {
                ProcessOAuthError(new Exception(SdkException, ex), SDK.OAuthRequestType.OAuthTypeAuth);
            }
        }

        private void BeginOAuthRequest(SDK.OAuthRequestType type)
        {
            try
            {
                Uri myUri = new Uri(UriTokenRequest);
                HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create(myUri);
                request.Method = "POST";
                request.ContentType = "application/x-www-form-urlencoded";
                request.BeginGetRequestStream(arg => BeginGetOAuthResponse(arg, type), request);

            }
            catch (Exception e)
            {
                ProcessOAuthError(new Exception(SdkException, e), type);
            }
        }

        private void BeginGetOAuthResponse(IAsyncResult result, SDK.OAuthRequestType type)
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)result.AsyncState; 
                Stream postStream = request.EndGetRequestStream(result);

                string parameters = null;
                if (type == SDK.OAuthRequestType.OAuthTypeAuth)
                {
                    parameters = String.Format(DataTemplateAuthTokenRequest, new object[] {this._code, this._redirectUrl, this._appId, this._appSecretKey});
                }
                else if (type == SDK.OAuthRequestType.OAuthTypeUpdateToken)
                {
                    parameters = String.Format(DataTemplateAuthTokenUpdateRequest, this._refreshToken, this._appId, this._appSecretKey);
                }
                byte[] byteArray = Encoding.UTF8.GetBytes(parameters);

                postStream.Write(byteArray, 0, byteArray.Length);
                postStream.Close();

                request.BeginGetResponse(arg => ProcessOAuthResponse(arg, type), request);
            }
            catch (Exception e)
            {
                ProcessOAuthError(new Exception(SdkException, e), type);
            }
        }

        private void ProcessOAuthResponse(IAsyncResult callbackResult, SDK.OAuthRequestType type)
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)callbackResult.AsyncState;
                HttpWebResponse response = (HttpWebResponse)request.EndGetResponse(callbackResult);
                using (StreamReader httpWebStreamReader = new StreamReader(response.GetResponseStream()))
                {
                    string result = httpWebStreamReader.ReadToEnd();
                    int tokenPosition = result.IndexOf(ParameterNameAccessToken);
                    if(tokenPosition != 0)
                    {
                        StringBuilder builder = new StringBuilder();
                        //plus length of (access_token":")
                        tokenPosition += 15;
                        while (tokenPosition < result.Length && !result[tokenPosition].Equals( '\"'))
                        {
                            builder.Append(result[tokenPosition]);
                            tokenPosition++;
                        }
                        this._accessToken = builder.ToString();
                        AuthCallbackStruct callbackStruct = this._updateCallback;
                        if (type == SDK.OAuthRequestType.OAuthTypeAuth)
                        {
                            builder.Clear();
                            //plus length of (refresh_token":")
                            tokenPosition = result.IndexOf(ParameterNameRefreshToken) + 16;
                            while (tokenPosition < result.Length && !result[tokenPosition].Equals('\"'))
                            {
                                builder.Append(result[tokenPosition]);
                                tokenPosition++;
                            }
                            this._refreshToken = builder.ToString();

                            callbackStruct = this._authCallback;
                        }
                        if (callbackStruct.SaveSession)
                        {
                            SaveSession();
                        }
                        if (callbackStruct.CallbackContext != null && callbackStruct.OnSuccess != null)
                        {
                            callbackStruct.CallbackContext.Dispatcher.BeginInvoke(() => callbackStruct.OnSuccess.Invoke());
                        }
                    }
                    else
                    {
                        ProcessOAuthError(new Exception(ErrorNoTokenSentByServer), type);
                    }
                }
            }
            catch (Exception e)
            {
                ProcessOAuthError(e, type);
            }
        }

        private void ProcessOAuthError(Exception e, SDK.OAuthRequestType type)
        {
            if (type == SDK.OAuthRequestType.OAuthTypeAuth && this._authCallback.OnError != null && this._authCallback.CallbackContext != null)
            {
                this._authCallback.CallbackContext.Dispatcher.BeginInvoke(() => this._authCallback.OnError.Invoke(e));
            }
            else if (type == SDK.OAuthRequestType.OAuthTypeUpdateToken && this._updateCallback.OnError != null)
            {
                this._updateCallback.CallbackContext.Dispatcher.BeginInvoke(() => this._updateCallback.OnError.Invoke(e));
            }
        }

        #endregion

        /**
         * Callback for SendRequest function.
         * Checks for errors and calls callback for each API request.
         */
        private void RequestCallback(IAsyncResult result)
        {
            HttpWebRequest request = result.AsyncState as HttpWebRequest;
            try
            {
                // if response == null, we'll get exception
                // ReSharper disable once PossibleNullReferenceException
                WebResponse response = request.EndGetResponse(result);
                string resultText = GetUtf8TextFromWebResponse(response);
                CallbackStruct callback = this._callbacks.SafeGet(request);
                this._callbacks.SafeRemove(request);
                if (resultText.IndexOf("\"error_code\":102") != -1)
                {
                    if (callback.OnError != null)
                    {
                        callback.OnError(new Exception(ErrorSessionExpired));
                        return;
                    }
                }
                else if (resultText.IndexOf("\"error_code\"") != -1)
                {
                    if (callback.OnError != null)
                    {
                        callback.OnError(new Exception(ErrorBadApiRequest + "  " + resultText));
                        return;
                    }
                }
                if (callback.CallbackContext != null && callback.OnSuccess != null)
                {
                    callback.CallbackContext.Dispatcher.BeginInvoke(() => callback.OnSuccess.Invoke(resultText));
                }    
            }
            catch (WebException e)
            {
                Action<Exception> onError = this._callbacks.SafeGet(request).OnError;
                this._callbacks.SafeRemove(request);
                if (onError != null)
                {
                    onError.Invoke(e);
                }
            }
        }

        private static string GetUtf8TextFromWebResponse(WebResponse response)
        {
            StringBuilder sb = new StringBuilder();
            Byte[] buf = new byte[8192];
            Stream resStream = response.GetResponseStream();
            int count;
            do
            {
                count = resStream.Read(buf, 0, buf.Length);
                if (count != 0)
                {
                    sb.Append(Encoding.UTF8.GetString(buf, 0, count));
                }
            } while (count > 0);
            return sb.ToString();
        }

        /**
         * Calculates signature for API request with given method and parameters.
         * @param method method name
         * @param parameters dictionary "parameter_name":"parameter_value"
         */
        private string CalcSignature(string method, Dictionary<string, string> parameters)
        {
            Dictionary<string, string> parametersLocal = parameters == null ? new Dictionary<string, string>() : new Dictionary<string, string>(parameters);

            parametersLocal.Add("application_key", this._appPublicKey);
            parametersLocal.Add("method", method);
            StringBuilder builder = new StringBuilder();
            foreach (KeyValuePair<string, string> pair in parametersLocal.OrderBy(item=>item.Key))
            {
                builder.Append(pair.Key).Append("=").Append(pair.Value);
            }
            string s = MD5.GetMd5String(this._accessToken.Insert(this._accessToken.Length, this._appSecretKey));
            return MD5.GetMd5String(builder.Append(s).ToString());
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
