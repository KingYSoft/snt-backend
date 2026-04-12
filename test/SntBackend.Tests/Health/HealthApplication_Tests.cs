using SntBackend.Application.Health;
using System.Threading.Tasks;
using Xunit;
namespace SntBackend.Tests.Health
{
    public class HealthApplication_Tests : SntBackendTestBase
    {
        private readonly IHealthApplication _healthApplication;
        public HealthApplication_Tests()
        {
            _healthApplication = Resolve<IHealthApplication>();
        }

        [Fact]
        public async Task Check_Test()
        {
            // Act
            var output = await _healthApplication.Check();

            // Assert
            // output.ShouldNotBeNull();
        }
    }
}
