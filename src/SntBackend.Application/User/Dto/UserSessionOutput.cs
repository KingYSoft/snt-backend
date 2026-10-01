namespace SntBackend.Application.User.Dto
{
    public class UserSessionOutput
    {
        public string staff_pk { get; set; }
        public string company_pk { get; set; }
        public string company_code { get; set; }
        public string company_name { get; set; }
        public string branch_pk { get; set; }
        public string branch_code { get; set; }
        public string branch_name { get; set; }
        public string dept_pk { get; set; }
        public string dept_code { get; set; }
        public string dept_name { get; set; }
    }
}
