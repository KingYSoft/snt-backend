using System.Collections.Generic;

namespace SntBackend.Application.Edoc.Dto
{
    public class EdocSearchInput
    {
        public string parent_table { get; set; }
        public string related_key { get; set; }
    }

    public class EdocSaveInput
    {
        public string parent_table { get; set; }
        public string related_key { get; set; }
        public string file_type { get; set; }
        public string ref_no { get; set; }
        public int is_ebooking_visible { get; set; }
    }

    public class EdocSearchOutput
    {
        public List<EdocItemOutput> list { get; set; } = new();
    }

    public class EdocItemOutput
    {
        public long id { get; set; }
        public string pk { get; set; }
        public string parent_table { get; set; }
        public string related_key { get; set; }
        public string file_name { get; set; }
        public string file_type { get; set; }
        public string file_path { get; set; }
        public string ref_no { get; set; }
        public string remarks { get; set; }
        public int is_ebooking_visible { get; set; }
        public int is_publish { get; set; }
        public string published_by { get; set; }
        public System.DateTime? published_time { get; set; }
        public System.DateTime? received_time { get; set; }
        public string create_user_name { get; set; }
        public string dept_code { get; set; }
        public int has_download { get; set; }
        public int has_preview { get; set; }
        public int has_gen_edi { get; set; }
        public int is_gen_edi { get; set; }
        public System.DateTime? gen_edi_time { get; set; }
    }
}
