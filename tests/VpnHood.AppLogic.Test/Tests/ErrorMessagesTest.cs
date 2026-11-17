using System.Globalization;
using System.Text;
using VpnHood.AppLogic.Api.Exceptions;
using VpnHood.AppUi.Common;
using VpnHood.Net.Toolkit.ApiClients;
using VpnHood.Net.Toolkit.Assets;

namespace VpnHood.AppLogic.Test.Tests;

// An error the API names is said in the person's language, never as the app's own English message.
[TestClass]
[DoNotParallelize] // the language is the process's
public class ErrorMessagesTest
{
    private sealed class WordsStore : IAssetProvider
    {
        private readonly Dictionary<string, string> _files = new() {
            ["locales/en.json"] = """
                { "UNREACHABLE_SERVER_MESSAGE": "No server", "REWARDED_AD_SHOW_ERROR_MSG": "No ad" }
                """,
            ["locales/fa.json"] = """
                { "UNREACHABLE_SERVER_MESSAGE": "سروری نیست", "REWARDED_AD_SHOW_ERROR_MSG": "تبلیغی نیست" }
                """,
            ["locales/index.json"] = """["en", "fa"]"""
        };

        public Task<Stream> OpenReadAsync(string assetPath, CancellationToken cancellationToken)
        {
            return _files.TryGetValue(assetPath, out var text)
                ? Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(text)))
                : throw new AssetNotFoundException(assetPath);
        }
    }

    [TestCleanup]
    public async Task BackToEnglish()
    {
        await Strings.Current.SetCultureAsync(CultureInfo.GetCultureInfo("en"), CancellationToken.None);
    }

    [TestMethod]
    public async Task An_ad_failure_and_an_unfound_server_are_said_in_the_persons_language()
    {
        await Strings.InitAsync(new WordsStore(), CancellationToken.None);
        await Strings.Current.SetCultureAsync(CultureInfo.GetCultureInfo("fa"), CancellationToken.None);

        var ad = ErrorMessages.For(Error(ExceptionType.Ad), new ErrorContext());
        Assert.AreEqual("تبلیغی نیست", Dialog(ad).Text);

        // as an unreachable server is: the diagnosis is offered
        var endPoint = ErrorMessages.For(Error(ExceptionType.EndPointDiscovery), new ErrorContext());
        Assert.AreEqual("سروری نیست", Dialog(endPoint).Text);
        CollectionAssert.Contains(Dialog(endPoint).Actions.ToArray(), ErrorAction.Diagnose);
    }

    // the error as it comes over the API: the class name the value stands for, and the app's English message
    private static ApiError Error(ExceptionType exceptionType)
    {
        var typeName = Enum.GetName(exceptionType) + "Exception";
        var apiError = new ApiError { TypeName = typeName, Message = "The app's own message" };
        Assert.AreEqual(exceptionType, apiError.GetExceptionType());
        return apiError;
    }

    private static ErrorMessage.Dialog Dialog(ErrorMessage message)
    {
        Assert.IsInstanceOfType<ErrorMessage.Dialog>(message);
        return (ErrorMessage.Dialog)message;
    }
}
