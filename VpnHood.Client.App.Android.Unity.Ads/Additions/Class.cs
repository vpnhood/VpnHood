//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;

//using Android.App;
//using Android.Content;
//using Android.OS;
//using Android.Runtime;
//using Android.Views;
//using Android.Widget;

//namespace Com.Unity3d.Services.Core.Request
//{
//    public partial interface IWebRequestListener
//    {
//        void OnFailed(string p0, string p1);
//    }

//    internal partial class IWebRequestListenerInvoker
//    {
//        static Delegate cb_onFailed_Ljava_lang_String_Ljava_lang_String_;
//#pragma warning disable 0169
//        static Delegate GetOnFailed_Ljava_lang_String_Ljava_lang_String_Handler()
//        {
//            if (cb_onFailed_Ljava_lang_String_Ljava_lang_String_ == null)
//                cb_onFailed_Ljava_lang_String_Ljava_lang_String_ = JNINativeWrapper.CreateDelegate((Action<IntPtr, IntPtr, IntPtr, IntPtr>)n_OnFailed_Ljava_lang_String_Ljava_lang_String_);
//            return cb_onFailed_Ljava_lang_String_Ljava_lang_String_;
//        }

//        static void n_OnFailed_Ljava_lang_String_Ljava_lang_String_(IntPtr jnienv, IntPtr native__this, IntPtr native_p0, IntPtr native_p1)
//        {
//            global::Com.Unity3d.Services.Core.Request.IWebRequestListener __this = global::Java.Lang.Object.GetObject<global::Com.Unity3d.Services.Core.Request.IWebRequestListener>(jnienv, native__this, JniHandleOwnership.DoNotTransfer);
//            string p0 = JNIEnv.GetString(native_p0, JniHandleOwnership.DoNotTransfer);
//            string p1 = JNIEnv.GetString(native_p1, JniHandleOwnership.DoNotTransfer);
//            __this.OnFailed(p0, p1);
//        }
//#pragma warning restore 0169

//        IntPtr id_onFailed_Ljava_lang_String_Ljava_lang_String_;
//        public unsafe void OnFailed(string p0, string p1)
//        {
//            if (id_onFailed_Ljava_lang_String_Ljava_lang_String_ == IntPtr.Zero)
//                id_onFailed_Ljava_lang_String_Ljava_lang_String_ = JNIEnv.GetMethodID(class_ref, "onFailed", "(Ljava/lang/String;Ljava/lang/String;)V");
//            IntPtr native_p0 = JNIEnv.NewString(p0);
//            IntPtr native_p1 = JNIEnv.NewString(p1);
//            JValue* __args = stackalloc JValue[2];
//            __args[0] = new JValue(native_p0);
//            __args[1] = new JValue(native_p1);
//            JNIEnv.CallVoidMethod(((global::Java.Lang.Object)this).Handle, id_onFailed_Ljava_lang_String_Ljava_lang_String_, __args);
//            JNIEnv.DeleteLocalRef(native_p0);
//            JNIEnv.DeleteLocalRef(native_p1);
//        }
//    }

//    public partial class FailedEventArgs
//    {
//        public FailedEventArgs(string p0, string p1)
//        {
//            this.p00 = p0;
//            this.p11 = p1;
//        }

//        string p00;
//        public string P00
//        {
//            get { return p00; }
//        }

//        string p11;
//        public string P11
//        {
//            get { return p11; }
//        }
//    }

//    internal sealed partial class IWebRequestListenerImplementor
//    {
//        public EventHandler<FailedEventArgs> OnFailedHandler;

//        public void OnFailed(string p0, string p1)
//        {
//            var __h = OnFailedHandler;
//            if (__h != null)
//                __h(sender, new FailedEventArgs(p0, p1));
//        }
//    }
//}