using Abp;
using Abp.Dependency;
using Castle.Facilities.Logging;
using Facade.NLogger;
using System;

namespace SntBackend.EntityGenerate
{
    public class Program
    {
        public static void Main(string[] args)
        {
            using (var bootstrapper = AbpBootstrapper.Create<SntBackendEntityGenerateModule>())
            {
                bootstrapper.IocManager.IocContainer.AddFacility<LoggingFacility>(f =>
                {
                    f.UseFacadeNLog($"{AppDomain.CurrentDomain.BaseDirectory}\\NLog.config");
                });

                bootstrapper.Initialize();

                using (var executer = bootstrapper.IocManager.ResolveAsDisposable<EntityGenerateExecuter>())
                {
                    var succeeded = executer.Object.Run();
                    if (succeeded)
                    {
                        Console.WriteLine("实体生成成功！");
                    }
                    else
                    {
                        Console.WriteLine("实体生成失败！");

                    }
                    Console.WriteLine("按任意键退出.....");
                    Console.ReadLine();

                }
            }
        }

    }
}
