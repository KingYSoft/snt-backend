namespace SntBackend.Application.User.Dto
{
    public class UserChangePasswordInput
    {
        public string oldPassword { get; set; }
        public string newPassword { get; set; }
        public string confirmPassword { get; set; }
    }
}
