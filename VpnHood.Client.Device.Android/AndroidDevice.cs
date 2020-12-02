using System;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;

namespace VpnHood.Client.Device.Android
{
    public class AndroidDevice : IDevice
    {
        private readonly EventWaitHandle _serviceWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
        private readonly EventWaitHandle _grantPermisssionWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
        private IPacketCapture _packetCapture;
        private bool _permissionGranted = false;

        public event EventHandler OnStartAsService;
        public event EventHandler OnRequestVpnPermission;

        public void VpnPermissionGranted()
        {
            _permissionGranted = true;
            _grantPermisssionWaitHandle.Set();
        }

        public void VpnPermissionRejected()
        {
            _grantPermisssionWaitHandle.Set();
        }

        public static AndroidDevice Current { get; private set; }
        public AndroidDevice()
        {
            if (Current != null) throw new InvalidOperationException($"Only one {nameof(AndroidDevice)} can be created!");
            Current = this;
        }

        public Task<IPacketCapture> CreatePacketCapture()
        {
            return Task.Run(() =>
            {
                // Grant for permission if OnRequestVpnPermission is registered otherwise let service throw the error
                if (OnRequestVpnPermission != null)
                {
                    _permissionGranted = false;
                    OnRequestVpnPermission.Invoke(this, EventArgs.Empty);
                    _grantPermisssionWaitHandle.WaitOne(10000);
                    if (!_permissionGranted)
                        throw new Exception("Could not grant VPN permission!");
                }

                StartService();
                _serviceWaitHandle.WaitOne();
                return Task.FromResult(_packetCapture);
            });
        }

        internal void OnServiceStartCommand(IPacketCapture packetCapture, Intent intent)
        {
            _packetCapture = packetCapture;
            _serviceWaitHandle.Set();

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