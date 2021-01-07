using System.Threading.Tasks;

namespace VpnHood.Client.Diagnosing
{
    public class DiagnoseTest
    {
        public DiagnoseTestType TestType { get; internal set; }
        public DiagnoseState State { get; internal set; } = DiagnoseState.None;
        public string ErrorMessage { get; internal set; } 
    }

}
