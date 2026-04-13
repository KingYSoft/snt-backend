using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SntBackend.EntityGenerate
{
    public class Consts
    {
        public const string DefaultNameOrConnectionString = "Server=1.15.55.23;Database=QI1PRD;User ID=sa;Password=MsSQL@2024;TrustServerCertificate=True;Pooling=True;Max Pool Size=1024;";
        // public const string DefaultNameOrConnectionString = "Server=localhost;Database=SjcDb;User ID=sa;Password=123456;TrustServerCertificate=True;Pooling=True;Max Pool Size=1024;";
        public const string DBName = "QI1PRD";

        public const string Namespace = "SntBackend";

        public const string DbContext = "SntBackendSqlServerDbContext";
    }
}
