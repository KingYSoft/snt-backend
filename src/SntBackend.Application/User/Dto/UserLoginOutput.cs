
using System.Collections.Generic;

namespace SntBackend.Application.User.Dto
{
    public class UserLoginOutput
    {
        public string accessToken { get; set; }
        public int expireInSeconds { get; set; } = 86400; // 默认 24 小时
        public string full_name { get; set; }
        public string login_name { get; set; }
        public string email_address { get; set; }
    }
}
