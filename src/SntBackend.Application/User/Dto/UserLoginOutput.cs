
using System.Collections.Generic;

namespace SntBackend.Application.User.Dto
{
    public class UserLoginOutput
    {
        public string access_token { get; set; }
        public string full_name { get; set; }
        public string login_name { get; set; }
        public string email_address { get; set; }
    }
}
