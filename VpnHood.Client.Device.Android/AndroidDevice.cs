using System;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Net;

namespace VpnHood.Client.Device.Android
{
    public class AndroidDevice : IDevice
    {
        private readonly EventWaitHandle _waitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
        private IPacketCapture _packetCapture;
        public event EventHandler OnStartAsService;

        public static AndroidDevice Current { get; private set; }
        public AndroidDevice()
        {
            if (Current != null) throw new InvalidOperationException($"Only one {nameof(AndroidDevice)} can be created!");
            Current = this;
        }

        public Task<IPacketCapture> CreatePacketCapture()
        {
            VpnService.Prepare(Application.Context);

            return Task.Run(() =>
            {
                StartService();
                _waitHandle.WaitOne();
                return Task.FromResult(_packetCapture);
            });
        }

        internal void OnStartCommand(IPacketCapture packetCapture, Intent intent)
        {
            _packetCapture = packetCapture;
            _waitHandle.Set();

            // fire AutoCreate for always on
            var manual = intent?.GetBooleanExtra("manual", false) ?? false;
            if (!manual)
                OnStartAsService?.Invoke(this, EventArgs.Empty);
        }

        private void StartService()
        {
            var intent = new Intent(Application.Context, typeof(AppVpnService));
            intent.PutExtra("manual", true);
            Application.Context.StartService(intent.SetAction("connect"));
        }
    }
}