using System;
using System.ComponentModel.DataAnnotations;

namespace Flexinets.iPass.Models
{
    public class InviteIpassModel
    {
        [Required]
        [DataType(DataType.EmailAddress)]
        public String email { get; set; }

        [Required]
        public Guid activationToken { get; set; }
    }
}
