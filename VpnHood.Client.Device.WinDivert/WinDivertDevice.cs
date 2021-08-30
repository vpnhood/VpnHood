using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VpnHood.Client.Device.WinDivert
{
    public class WinDivertDevice : IDevice
    {
#pragma warning disable 0067
        public event EventHandler? OnStartAsService;
#pragma warning restore 0067

        public string OperatingSystemInfo =>
            Environment.OSVersion + ", " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");

        public bool IsExcludeAppsSupported => false;

        public bool IsIncludeAppsSupported => false;

        public DeviceAppInfo[] InstalledApps
        {
            get
            {
#if DEBUG
                var list = new List<DeviceAppInfo>();
                for (var i = 0; i < 60; i++)
                    list.Add(new DeviceAppInfo(
                        $"AppId {i}",
                        $"My Name {i}",
                        "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAAXNSR0IArs4c6QAAAARzQklUCAgICHwIZIgAAAV7SURBVFiFpZdNjBzFFcd/r6pnMd7FblhjYT6SdQgXpOAx9gkhGIQwioF4TA4cYpN1gnLEIHxBfC0SEhyMWCGwhPhYIyAhFzwOUZCQjOeYm3ekcIrRLoIg2TjWLMvi2Zmu9zj0zGxPb6/XC2/U6uqq7vf/v3+9V1UjrMFOV++uCm6PYWOCxGBlAMymDW0KMuvUals/Pnn8Un3Kai/MVCuxutIrGFUgHhg0y9wH2k0zat7z+NZavfmTCXzx4K4JzA4iEg8Apg8pZr8vS6B3s6YYkzf+s/78mgjMVCtxh+ik9748GGUWP99fRCBtmDEdlbirSA2X7/h8d6U8H5hR07IFXYn4mkzEyiGxmZndt5WXjWUfTlXKMcNXzHhxceQckTicc8tlWkP0A+Nqs/4Ht31rfUmJAQXal62rq1qspgQzAobmpf85Jowl6/VktqtPYOfbj0wobAsaUDPUFFXFTDEyJFaJnlX4CpT/u/uOiQECY1Pj8bkhDs6GNGJVQ1UJpmk7aC7jwcxom9EKunSpciEEWqoshoTFoLQ06Y6n/S1VWkEPzlTKMUAEQCmaFCR+avwWXn2nweYIHB4VQy0g3iOmOBGaUYlfv/1hYXRnP9nHUHIO78E5iLwgYvhIcE5YNwQ+EkQkRoYnqTMedb+tWlfoxvUb2PHNPKOiSDCC84gaIoHRNz5g0/AIAM8cE2bOpTN4zQbj8EPK5t++j4U2Cyf24JwwdBkMX+4AQRwg0p8H8HsAZOy9P1cROdaXFuPIOw2GnOdK70irwbPlzb8RjYzw3HHHF2eX6iI79VcNG6/9QTELjEzv7YMJYC5VA5H0ZSc4bK9TqJoZvQuDI7tuQlX5LgTUoOWEaGSEiZoUgvfs/ILw6F8dIp7v/S+QyCGRg5JDIoHII94hJY9EDo181YnZWH4u/3PdelQDZsZ8CNz0wT8AOP3tUtVmwbNl/+18SlBueQ0iD5FDIp/mkXcpiS4xF7kxh+Q2mK4dHC+nZagBgJf+VRy5LWvAI0dTohJJFzwFFO8QJ9nlb6MD2ZYFtu6v5eF8JKDpcvz5N7IyeK69sNh9VzwicpEtz8rOGPxlXR7a/5s0L7oIhbKzPBeWTFi+Mg0+O4NGfyiTjD2nx8pbALj5Wit2UQA+PGRd+GQlZj3qDWdmzX4F5IeBj27dDMCT91m/LyVbHLkZvHUgnTaRvPa55RtpOjFm86B5v7cfPwTAL0e1D1IYk6VrAYDMvrACeLZLp11wVisCzebFeTrMdy7w4u+NG68uRu+BH9mvBFWuCI0Bb8vaZmBSF4Dr3z3QNGHjSltZb3pOPfAy63wJgEN/d/yvmUp81Xrj9f2pOkGVK7/6CyRzBQQy/tXmZOeJOAJQrCbwx2Ua5Phs//gJbhjexKf3PMvhh5afln734dN8fWaW0/cvXhw8dVyDboXGU+PxerFZhI2FElAwRWaUnAeD9g8tWGhjnYQz9y4CWvBVVnrm8GFMttebDqB54GgTscki0Hx+ZCumrQmLoQ2tBFNl7ya/OjiA2KRsT49lA3Wy5d2Hp/Mr49J3+TLtnv86AZtrQSfh7L2tYuAsONaQHZ/1D6cDZ8IL6ipmzGUjteyfjtyKaapoJwFV/n3XhksB/xKnlSzmAIHmgaPNYJ2KqmZTeIWFGjQJ0A5ICPxq6MylgFd70vescJuIp8bjIZK6SH6jyrSTgLUTbKHFuV0lCN9dDLyB00oefEUCPds8tW8CeAyRgeowM3SxDe2EG5KEU5UL5HOkn+1ik7Ljs4mVMFb9cxpPjcclkkmgKiIbzQzrJOnV7vD/Oxchu+n0gKGGD48VRb0mAlkbndpXJYQq7WRMO0n8p1G37fDNc4A1MGsC05jUZeeJ2qX6/BFJwTP1ncDguQAAAABJRU5ErkJggg=="
                    ));
                return list.ToArray();
#else
                throw new NotSupportedException();
#endif
            }
        }

        public Task<IPacketCapture> CreatePacketCapture()
        {
            var res = (IPacketCapture) new WinDivertPacketCapture();
            return Task.FromResult(res);
        }
    }
}