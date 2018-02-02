using Flexinets.Common;
using Flexinets.iPass.Models;
using Flexinets.Portal.Models;
using log4net;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Flexinets.iPass
{
    public class iPassProvisioningApiClient
    {
        private readonly ILog _log = LogManager.GetLogger(typeof(iPassProvisioningApiClient));
        private readonly String _ipassApiKey;
        private readonly String _flexinetsApiUsername;
        private readonly String _flexinetsApiPassword;


        /// <summary>
        /// Handles provisioning of hosted users
        /// </summary>
        /// <param name="contextFactory"></param>
        public iPassProvisioningApiClient(String ipassApiKey, String flexinetsApiUsername, String flexinetsApiPassword)
        {
            _ipassApiKey = ipassApiKey;
            _flexinetsApiUsername = flexinetsApiUsername;
            _flexinetsApiPassword = flexinetsApiPassword;
        }


        /// <summary>
        /// Create an authenticated httpclient with api key
        /// </summary>
        /// <returns></returns>
        private HttpClient CreateAuthenticatedHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("x-ipass-key", _ipassApiKey);
            return client;
        }


        /// <summary>
        /// Create authenticated client with customerid
        /// </summary>
        /// <param name="customerId"></param>
        /// <returns></returns>
        private HttpClient CreateAuthenticatedHttpClient(Int32 customerId)
        {
            var client = CreateAuthenticatedHttpClient();
            client.DefaultRequestHeaders.Add("x-ipass-company-id", customerId.ToString());
            return client;
        }


        /// <summary>
        /// Update existing user
        /// Returns the hostedUserId if successful
        /// </summary>
        /// <param name="customerId"></param>
        /// <param name="user"></param>
        /// <returns></returns>
        public async Task<String> UpdateUserAsync(Int32 customerId, UserModel user)
        {
            var url = new Uri("https://api.ipass.com/v1/users?service=update");

            var name = Utils.SplitFullname(user.Fullname);
            var content = new XDocument(new XDeclaration("1.0", "utf-8", "yes"),
                                        new XElement("endUser",
                                           new XElement("fname") { Value = name.firstname },
                                           new XElement("lname") { Value = name.lastname },
                                           new XElement("email") { Value = user.EmailAddress },
                                           new XElement("homeCountry") { Value = "AX" },
                                           new XElement("locale") { Value = "en-US" },
                                           new XElement("username") { Value = user.UsernameDomain },
                                           new XElement("password") { Value = "" },
                                           new XElement("enablePortalLogin") { Value = "false" },
                                           new XElement("departmentCode") { Value = "" },
                                           new XElement("notifications",
                                               new XElement("notification",
                                                   new XAttribute("subscribe", "false"),
                                                   new XElement("type") { Value = "Activate" }),
                                               new XElement("notification",
                                                   new XAttribute("subscribe", "false"),
                                                   new XElement("type") { Value = "Suspend" }))
                                           )).ToString();

            using (var client = CreateAuthenticatedHttpClient(customerId))
            {
                var response = await client.PostAsync(url, new StringContent(content, Encoding.UTF8));
                var responseXml = XDocument.Parse(await response.Content.ReadAsStringAsync());
                var userid = responseXml.Root.Element("endUserId").Value;
                return userid;
            }
        }


        /// <summary>
        /// Create a user
        /// Returns the hostedUserId if successful
        /// </summary>
        /// <param name="customerId"></param>
        /// <param name="user"></param>
        /// <returns></returns>
        public async Task<(String hostedUserId, String activationUrl)> CreateUserAsync(Int32 customerId, UserModel user, Boolean sendInvite = true)
        {
            var url = new Uri("https://api.ipass.com/v1/users?service=create");
            if (String.IsNullOrEmpty(user.Fullname))
            {
                user.Fullname = "Jone Doe";
            }
            var name = Utils.SplitFullname(user.Fullname);
            if (String.IsNullOrEmpty(name.firstname))   // ipass requires both to be non empty
            {
                name.firstname = name.lastname;
            }
            var usernamedomain = $"{user.Username}@{user.Domain}";
            var content = new XDocument(new XDeclaration("1.0", "utf-8", "yes"),
                                        new XElement("endUser",
                                           new XElement("fname") { Value = name.firstname },
                                           new XElement("lname") { Value = name.lastname },
                                           new XElement("email") { Value = user.EmailAddress },
                                           new XElement("homeCountry") { Value = "AX" },
                                           new XElement("locale") { Value = "en-US" },
                                           new XElement("username") { Value = usernamedomain },
                                           new XElement("password") { Value = "" },
                                           new XElement("enablePortalLogin") { Value = "false" },
                                           new XElement("departmentCode") { Value = "" },
                                           new XElement("notifications",
                                               new XElement("notification",
                                                   new XAttribute("subscribe", "false"),
                                                   new XElement("type") { Value = "Activate" }),
                                               new XElement("notification",
                                                   new XAttribute("subscribe", "false"),
                                                   new XElement("type") { Value = "Suspend" }))
                                           )).ToString();

            using (var client = CreateAuthenticatedHttpClient(customerId))
            {
                var response = await client.PostAsync(url, new StringContent(content, Encoding.UTF8));
                var responseXml = XDocument.Parse(await response.Content.ReadAsStringAsync());
                var userid = responseXml.Root.Element("endUserId").Value;
                var activationUrl = responseXml.Root.Element("selfServiceActivationUrl").Value;

                if (sendInvite)
                {
                    await SendInviteAsync(new InviteIpassModel { email = user.EmailAddress, activationUrl = activationUrl });
                }

                return (userid, activationUrl);
            }
        }


        /// <summary>
        /// Get a hosted ipass user
        /// </summary>
        /// <param name="customerId"></param>
        /// <param name="usernamedomain"></param>
        /// <returns></returns>
        public async Task<String> GetUserAsync(Int32 customerId, String usernamedomain)
        {
            var url = $"https://api.ipass.com/v1/users?service=search&searchCriteria={usernamedomain}&page=1&limit=2";
            using (var client = CreateAuthenticatedHttpClient(customerId))
            {
                var response = await client.GetStringAsync(url);
                return response;
            }
        }


        /// <summary>
        /// Suspend a user
        /// </summary>
        /// <param name="customerId"></param>
        /// <param name="usernamedomain"></param>
        /// <returns></returns>
        public async Task<String> SuspendUserAsync(Int32 customerId, String usernamedomain)
        {
            var url = "https://api.ipass.com/v1/users?service=suspend";

            var content = new XDocument(new XDeclaration("1.0", "utf-8", "yes"),
                                     new XElement("endUser",
                                        new XElement("username") { Value = usernamedomain })).ToString();

            using (var client = CreateAuthenticatedHttpClient(customerId))
            {
                var response = await client.PostAsync(url, new StringContent(content, Encoding.UTF8));
                var responseXml = XDocument.Parse(await response.Content.ReadAsStringAsync());
                return responseXml.ToString();
            }
        }


        /// <summary>
        /// Delete a user
        /// </summary>
        /// <param name="customerId"></param>
        /// <param name="usernamedomain"></param>
        /// <returns></returns>
        public async Task<String> DeleteUserAsync(Int32 customerId, String usernamedomain)
        {
            var url = "https://api.ipass.com/v1/users?service=delete";

            var content = new XDocument(new XDeclaration("1.0", "utf-8", "yes"),
                                     new XElement("endUser",
                                        new XElement("username") { Value = usernamedomain })).ToString();

            using (var client = CreateAuthenticatedHttpClient(customerId))
            {
                var response = await client.PostAsync(url, new StringContent(content, Encoding.UTF8));
                var responseXml = XDocument.Parse(await response.Content.ReadAsStringAsync());
                return responseXml.ToString();
            }
        }


        /// <summary>
        /// Activate a user
        /// </summary>
        /// <param name="customerId"></param>
        /// <param name="usernamedomain"></param>
        /// <param name="email"></param>
        /// <returns></returns>
        public async Task<XDocument> ActivateUserAsync(Int32 customerId, String usernamedomain)
        {
            var url = "https://api.ipass.com/v1/users?service=activate";

            var content = new XDocument(new XDeclaration("1.0", "utf-8", "yes"),
                                     new XElement("endUser",
                                        new XElement("username") { Value = usernamedomain })).ToString();

            using (var client = CreateAuthenticatedHttpClient(customerId))
            {
                var response = await client.PostAsync(url, new StringContent(content, Encoding.UTF8));
                return XDocument.Parse(await response.Content.ReadAsStringAsync());
            }
        }


        /// <summary>
        /// Suspending and activating the user will generate a new activation url
        /// </summary>
        /// <param name="customerId"></param>
        /// <param name="usernamedomain"></param>
        /// <returns></returns>
        public async Task<String> RefreshActivationUrl(Int32 customerId, String usernamedomain)
        {
            await SuspendUserAsync(customerId, usernamedomain);
            var response = await ActivateUserAsync(customerId, usernamedomain);
            return response.Root.Element("selfServiceActivationUrl").Value;
        }


        /// <summary>
        /// Send invites to multiple users
        /// </summary>
        /// <returns></returns>
        public async Task SendInviteAsync(IEnumerable<InviteIpassModel> list)
        {
            using (var client = new HttpClient(new HttpClientHandler { Credentials = new NetworkCredential(_flexinetsApiUsername, _flexinetsApiPassword) }))
            {
                var json = JsonConvert.SerializeObject(list);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await client.PostAsync("https://api.flexinets.se/api/ipass/sendinvite", content);
            }
        }


        /// <summary>
        /// Send invite to single user
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        public async Task SendInviteAsync(InviteIpassModel model)
        {
            await SendInviteAsync(new List<InviteIpassModel> { model });
        }
    }
}