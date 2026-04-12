using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SntBackend.Application.User.Dto
{
    public class UserLoginInput
    {
        public string email { get; set; }
        public string password { get; set; }
    }
}
