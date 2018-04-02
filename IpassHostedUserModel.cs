using System;
using System.ComponentModel.DataAnnotations;

namespace Flexinets.iPass
{
    public class IpassHostedUserModel
    {
        public virtual String Username
        {
            get;
        }

        public virtual String Domain
        {
            get;
        }

        [StringLength(50, ErrorMessage = "Maximum length 100 characters")]
        [Display(Name = "Full name")]
        public String Fullname
        {
            get;
            internal set;
        }

        [StringLength(150, ErrorMessage = "Maximum length 150 characters")]
        [Display(Name = "Email address")]
        [DataType(DataType.EmailAddress)]
        public virtual String EmailAddress
        {
            get;
            internal set;
        }

        [Display(Name = "Password")]
        [DataType(DataType.Password)]
        public virtual String Password
        {
            get;
        }

        public String HostedAuthId
        {
            get;
            internal set;
        }

        public String HostedAuthUrl
        {
            get;
            internal set;
        }

        public String UsernameDomain => $"{Username}@{Domain}";


        public IpassHostedUserModel(String username, String domain, String email, String name, String password = null)
        {
            Username = username;
            Domain = domain;
            EmailAddress = email;
            Fullname = name;
            Password = password;
        }

        internal IpassHostedUserModel()
        {

        }
    }
}
