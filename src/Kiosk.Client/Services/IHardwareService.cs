using System.Threading.Tasks;
using KioskClinicaPC.Core;

namespace KioskClinicaPC.Services
{
    public interface IHardwareService
    {
        Task<AppConfig> GetHardwareInfoAsync();
    }
}