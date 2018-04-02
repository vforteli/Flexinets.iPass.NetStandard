using Flexinets.Common;
using Flexinets.iPass.Models;
using log4net;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace Flexinets.iPass
{
    public class iPassProvisioningApiClient
    {
        private readonly ILog _log = LogManager.GetLogger(typeof(iPassProvisioningApiClient));
        private readonly String _ipassApiKey;
        private readonly String _serviceBusConnectionString;


        /// <summary>
        /// Handles provisioning of hosted users
        /// </summary>
        /// <param name="contextFactory"></param>
        public iPassProvisioningApiClient(String ipassApiKey, String serviceBusConnectionString)
        {
            if (String.IsNullOrEmpty(ipassApiKey) || String.IsNullOrEmpty(serviceBusConnectionString))
            {
                throw new InvalidOperationException("Missing configuration");
            }
            _ipassApiKey = ipassApiKey;
            _serviceBusConnectionString = serviceBusConnectionString;
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
        /// Get a hosted ipass user 
        /// </summary> 
        /// <param name="customerId"></param> 
        /// <param name="usernamedomain"></param> 
        /// <returns></returns> 
        public async Task<IpassHostedUserModel> GetUserAsync(Int32 customerId, String usernamedomain)
        {
            var url = $"https://api.ipass.com/v1/users?service=search&searchCriteria={usernamedomain}&page=1&limit=1";
            using (var client = CreateAuthenticatedHttpClient(customerId))
            {
                var document = XDocument.Parse(await client.GetStringAsync(url));
                var endUser = document.Descendants("endUser").SingleOrDefault();
                return new IpassHostedUserModel
                {
                    EmailAddress = endUser.Element("email").Value,
                    Fullname = endUser.Element("fname") + " " + endUser.Element("lname")
                };
            }
        }


        /// <summary>
        /// Update existing user
        /// Returns the hostedUserId if successful
        /// </summary>
        /// <param name="customerId"></param>
        /// <param name="user"></param>
        /// <returns></returns>
        public async Task<String> UpdateUserAsync(Int32 customerId, IpassHostedUserModel user)
        {
            var url = new Uri("https://api.ipass.com/v1/users?service=update");

            var (firstname, lastname) = Utils.SplitFullname(user.Fullname);
            var enduser = new XElement("endUser",
                                           new XElement("fname") { Value = firstname },
                                           new XElement("lname") { Value = lastname },
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
                                                   new XElement("type") { Value = "Suspend" })));

            if (user.EmailAddress != null)
            {
                enduser.Add(new XElement("email") { Value = user.EmailAddress });
            }

            var content = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), enduser);

            using (var client = CreateAuthenticatedHttpClient(customerId))
            {
                var response = await client.PostAsync(url, new StringContent(content.ToString(), Encoding.UTF8));
                var document = XDocument.Parse(await response.Content.ReadAsStringAsync());
                if (document.Descendants().SingleOrDefault(o => o.Name == "errorCode")?.Value == "2005")
                {
                    _log.Warn($"Duplicate email for hosted user with email {user.EmailAddress}");
                    content.Element("endUser").Element("email").Value = user.EmailAddress.Replace("@", $"+{new Random().Next()}@");
                    response = await client.PostAsync(url, new StringContent(content.ToString(), Encoding.UTF8));
                    document = XDocument.Parse(await response.Content.ReadAsStringAsync());
                }

                if (document.Descendants().Any(o => o.Name == "error"))
                {
                    _log.Error($"Something went wrong creating hosted user\n{document}");
                    throw new InvalidOperationException($"Couldnt create hosted user: {document.Descendants().SingleOrDefault(o => o.Name == "errorMessage").Value}");
                }

                return document.Descendants("endUserId").SingleOrDefault().Value;
            }
        }


        /// <summary>
        /// Create a user
        /// Returns the hostedUserId if successful
        /// </summary>
        /// <param name="customerId"></param>
        /// <param name="user"></param>
        /// <returns></returns>
        public async Task<(String hostedUserId, String activationUrl)> CreateUserAsync(Int32 customerId, IpassHostedUserModel user)
        {
            if (String.IsNullOrEmpty(user.Fullname))
            {
                user.Fullname = "Jone Doe";
            }
            var (firstname, lastname) = Utils.SplitFullname(user.Fullname);
            if (String.IsNullOrEmpty(firstname))   // ipass requires both to be non empty
            {
                firstname = lastname;
            }
            var content = new XDocument(new XDeclaration("1.0", "utf-8", "yes"),
                                        new XElement("endUser",
                                           new XElement("fname") { Value = firstname },
                                           new XElement("lname") { Value = lastname },
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
                                           ));

            var url = new Uri("https://api.ipass.com/v1/users?service=create");
            using (var client = CreateAuthenticatedHttpClient(customerId))
            {
                var response = await client.PostAsync(url, new StringContent(content.ToString(), Encoding.UTF8));
                var document = XDocument.Parse(await response.Content.ReadAsStringAsync());
                if (document.Descendants().SingleOrDefault(o => o.Name == "errorCode")?.Value == "2005")
                {
                    _log.Warn($"Duplicate email for hosted user with email {user.EmailAddress}");
                    content.Element("endUser").Element("email").Value = user.EmailAddress.Replace("@", $"+{new Random().Next()}@");
                    response = await client.PostAsync(url, new StringContent(content.ToString(), Encoding.UTF8));
                    document = XDocument.Parse(await response.Content.ReadAsStringAsync());
                }

                if (document.Descendants().Any(o => o.Name == "error"))
                {
                    _log.Error($"Something went wrong creating hosted user\n{document}");
                    throw new InvalidOperationException($"Couldnt create hosted user: {document.Descendants().SingleOrDefault(o => o.Name == "errorMessage").Value}");
                }

                return (document.Descendants("endUserId").SingleOrDefault().Value, document.Descendants("selfServiceActivationUrl").SingleOrDefault().Value);
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
            // todo jfc it cant be this hard?
            var serializer = new DataContractSerializer(typeof(IEnumerable<InviteIpassModel>));
            var ms = new MemoryStream();
            using (var binaryDictionaryWriter = XmlDictionaryWriter.CreateBinaryWriter(ms))
            {
                serializer.WriteObject(binaryDictionaryWriter, list);
                binaryDictionaryWriter.Flush();
                var message = new Message(ms.ToArray());

                var messageSender = new MessageSender(new ServiceBusConnectionStringBuilder(_serviceBusConnectionString));
                await messageSender.SendAsync(message);
            }
        }


        /// <summary>
        /// Send invite to single user
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        public async Task SendInviteAsync(String email, Guid activationToken)
        {
            await SendInviteAsync(new List<InviteIpassModel> { new InviteIpassModel { activationToken = activationToken, email = email } });
        }
    }
}