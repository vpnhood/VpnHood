using VpnHood.Core.DomainFiltering;

namespace VpnHood.Test.Tests;

[TestClass]
public class DomainFilterResolverTest : TestBase
{
    [TestMethod]
    public void Process_EmptyFilter_ReturnsNone()
    {
        var domainFilter = new DomainFilter();
        var resolver = new DomainFilterResolver(domainFilter);

        Assert.AreEqual(DomainFilterAction.None, resolver.Process("example.com"));
        Assert.AreEqual(DomainFilterAction.None, resolver.Process("www.example.com"));
        Assert.AreEqual(DomainFilterAction.None, resolver.Process(null));
        Assert.AreEqual(DomainFilterAction.None, resolver.Process(""));
        Assert.AreEqual(DomainFilterAction.None, resolver.Process("   "));
    }

    [TestMethod]
    public void Process_ExactMatch_ReturnsCorrectAction()
    {
        var domainFilter = new DomainFilter {
            Blocks = ["block.com"],
            Excludes = ["exclude.com"],
            Includes = ["include.com"]
        };
        var resolver = new DomainFilterResolver(domainFilter);

        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("block.com"));
        Assert.AreEqual(DomainFilterAction.Exclude, resolver.Process("exclude.com"));
        Assert.AreEqual(DomainFilterAction.Include, resolver.Process("include.com"));
    }

    [TestMethod]
    public void Process_CaseInsensitive_ReturnsCorrectAction()
    {
        var domainFilter = new DomainFilter {
            Blocks = ["Block.COM"],
            Excludes = ["EXCLUDE.com"],
            Includes = ["InClUdE.CoM"]
        };
        var resolver = new DomainFilterResolver(domainFilter);

        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("block.com"));
        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("BLOCK.COM"));
        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("BlOcK.cOm"));

        Assert.AreEqual(DomainFilterAction.Exclude, resolver.Process("exclude.com"));
        Assert.AreEqual(DomainFilterAction.Exclude, resolver.Process("EXCLUDE.COM"));

        Assert.AreEqual(DomainFilterAction.Include, resolver.Process("include.com"));
        Assert.AreEqual(DomainFilterAction.Include, resolver.Process("INCLUDE.COM"));
    }

    [TestMethod]
    public void Process_WildcardDomain_MatchesSubdomains()
    {
        var domainFilter = new DomainFilter {
            Blocks = ["*.block.com"],
            Excludes = ["*.exclude.com"],
            Includes = ["*.include.com"]
        };
        var resolver = new DomainFilterResolver(domainFilter);

        // Exact match (wildcard means *.domain.com includes domain.com itself)
        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("block.com"));
        Assert.AreEqual(DomainFilterAction.Exclude, resolver.Process("exclude.com"));
        Assert.AreEqual(DomainFilterAction.Include, resolver.Process("include.com"));

        // Subdomain match
        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("www.block.com"));
        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("api.block.com"));
        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("deep.sub.block.com"));

        Assert.AreEqual(DomainFilterAction.Exclude, resolver.Process("www.exclude.com"));
        Assert.AreEqual(DomainFilterAction.Exclude, resolver.Process("api.exclude.com"));

        Assert.AreEqual(DomainFilterAction.Include, resolver.Process("www.include.com"));
        Assert.AreEqual(DomainFilterAction.Include, resolver.Process("api.include.com"));
    }

    [TestMethod]
    public void Process_WildcardDomain_DoesNotMatchParentDomain()
    {
        var domainFilter = new DomainFilter {
            Blocks = ["*.sub.example.com"]
        };
        var resolver = new DomainFilterResolver(domainFilter);

        // Should match
        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("sub.example.com"));
        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("www.sub.example.com"));
        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("api.sub.example.com"));

        // Should NOT match parent domain
        Assert.AreEqual(DomainFilterAction.None, resolver.Process("example.com"));
        Assert.AreEqual(DomainFilterAction.None, resolver.Process("other.example.com"));
    }

    [TestMethod]
    public void Process_PriorityOrder_BlockBeforeExclude()
    {
        var domainFilter = new DomainFilter {
            Blocks = ["example.com"],
            Excludes = ["example.com"]
        };
        var resolver = new DomainFilterResolver(domainFilter);

        // Block takes priority
        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("example.com"));
    }

    [TestMethod]
    public void Process_PriorityOrder_ExcludeBeforeInclude()
    {
        var domainFilter = new DomainFilter {
            Excludes = ["example.com"],
            Includes = ["example.com"]
        };
        var resolver = new DomainFilterResolver(domainFilter);

        // Exclude takes priority
        Assert.AreEqual(DomainFilterAction.Exclude, resolver.Process("example.com"));
    }

    [TestMethod]
    public void Process_PriorityOrder_BlockBeforeExcludeBeforeInclude()
    {
        var domainFilter = new DomainFilter {
            Blocks = ["example.com"],
            Excludes = ["example.com"],
            Includes = ["example.com"]
        };
        var resolver = new DomainFilterResolver(domainFilter);

        // Block takes priority over all
        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("example.com"));
    }

    [TestMethod]
    public void Process_WhitespaceHandling_TrimsAndProcesses()
    {
        var domainFilter = new DomainFilter {
            Blocks = ["  block.com  "],
            Excludes = [" exclude.com"],
            Includes = ["include.com "]
        };
        var resolver = new DomainFilterResolver(domainFilter);

        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("block.com"));
        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("  block.com  "));

        Assert.AreEqual(DomainFilterAction.Exclude, resolver.Process("exclude.com"));
        Assert.AreEqual(DomainFilterAction.Include, resolver.Process("include.com"));
    }

    [TestMethod]
    public void Process_MultipleWildcards_MatchesCorrectly()
    {
        var domainFilter = new DomainFilter {
            Blocks = ["*.google.com", "*.facebook.com", "*.twitter.com"]
        };
        var resolver = new DomainFilterResolver(domainFilter);

        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("google.com"));
        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("www.google.com"));
        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("mail.google.com"));

        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("facebook.com"));
        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("api.facebook.com"));

        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("twitter.com"));
        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("api.twitter.com"));

        Assert.AreEqual(DomainFilterAction.None, resolver.Process("example.com"));
    }

    [TestMethod]
    public void DomainFilter_Setter_UpdatesInternalArrays()
    {
        var domainFilter1 = new DomainFilter {
            Blocks = ["block1.com"]
        };
        var resolver = new DomainFilterResolver(domainFilter1);

        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("block1.com"));
        Assert.AreEqual(DomainFilterAction.None, resolver.Process("block2.com"));

        // Update the domain filter
        var domainFilter2 = new DomainFilter {
            Blocks = ["block2.com"]
        };
        resolver.DomainFilter = domainFilter2;

        Assert.AreEqual(DomainFilterAction.None, resolver.Process("block1.com"));
        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("block2.com"));
    }

    [TestMethod]
    public void Process_ComplexScenario_ReturnsCorrectActions()
    {
        var domainFilter = new DomainFilter {
            Blocks = ["*.ads.com", "tracker.example.com"],
            Excludes = ["*.internal.company.com", "localhost"],
            Includes = ["*.cdn.cloudflare.com", "api.service.com"]
        };
        var resolver = new DomainFilterResolver(domainFilter);

        // Blocks
        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("ads.com"));
        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("www.ads.com"));
        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("tracker.example.com"));

        // Excludes
        Assert.AreEqual(DomainFilterAction.Exclude, resolver.Process("internal.company.com"));
        Assert.AreEqual(DomainFilterAction.Exclude, resolver.Process("app.internal.company.com"));
        Assert.AreEqual(DomainFilterAction.Exclude, resolver.Process("localhost"));

        // Includes
        Assert.AreEqual(DomainFilterAction.Include, resolver.Process("cdn.cloudflare.com"));
        Assert.AreEqual(DomainFilterAction.Include, resolver.Process("static.cdn.cloudflare.com"));
        Assert.AreEqual(DomainFilterAction.Include, resolver.Process("api.service.com"));

        // None
        Assert.AreEqual(DomainFilterAction.None, resolver.Process("example.com"));
        Assert.AreEqual(DomainFilterAction.None, resolver.Process("www.example.com"));
    }

    [TestMethod]
    public void Process_DeepSubdomains_MatchesWildcard()
    {
        var domainFilter = new DomainFilter {
            Blocks = ["*.example.com"]
        };
        var resolver = new DomainFilterResolver(domainFilter);

        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("example.com"));
        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("www.example.com"));
        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("api.www.example.com"));
        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("deep.sub.domain.example.com"));
        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("very.deep.sub.domain.example.com"));
    }

    [TestMethod]
    public void Process_SpecificAndWildcard_BothWork()
    {
        var domainFilter = new DomainFilter {
            Blocks = ["specific.example.com", "*.wildcard.com"]
        };
        var resolver = new DomainFilterResolver(domainFilter);

        // Specific match
        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("specific.example.com"));
        Assert.AreEqual(DomainFilterAction.None, resolver.Process("other.example.com"));

        // Wildcard match
        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("wildcard.com"));
        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("www.wildcard.com"));
        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("any.wildcard.com"));
    }

    [TestMethod]
    public void Process_EmptyStringInArray_IgnoredCorrectly()
    {
        var domainFilter = new DomainFilter {
            Blocks = ["", "  ", "block.com", ""]
        };
        var resolver = new DomainFilterResolver(domainFilter);

        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("block.com"));
        Assert.AreEqual(DomainFilterAction.None, resolver.Process("example.com"));
    }

    [TestMethod]
    public void Process_NullAndEmptyInput_ReturnsNone()
    {
        var domainFilter = new DomainFilter {
            Blocks = ["block.com"]
        };
        var resolver = new DomainFilterResolver(domainFilter);

        Assert.AreEqual(DomainFilterAction.None, resolver.Process(null));
        Assert.AreEqual(DomainFilterAction.None, resolver.Process(""));
        Assert.AreEqual(DomainFilterAction.None, resolver.Process("   "));
        Assert.AreEqual(DomainFilterAction.None, resolver.Process("\t"));
    }

    [TestMethod]
    public void Process_SingleLabelDomain_WorksCorrectly()
    {
        var domainFilter = new DomainFilter {
            Blocks = ["localhost", "*.local"]
        };
        var resolver = new DomainFilterResolver(domainFilter);

        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("localhost"));
        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("LOCALHOST"));
        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("local"));
        Assert.AreEqual(DomainFilterAction.Block, resolver.Process("my-server.local"));
    }
}
