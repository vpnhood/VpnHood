using VpnHood.Core.SniFiltering;

namespace VpnHood.Test.Tests;

[TestClass]
public class DomainFilterResolverTest : TestBase
{
    [TestMethod]
    public void Process_EmptyFilter_ReturnsNone()
    {
        var domainFilter = new SniFilterPolicy();
        var resolver = new SniFilterResolver(domainFilter);

        Assert.AreEqual(SniFilterAction.None, resolver.Process("example.com"));
        Assert.AreEqual(SniFilterAction.None, resolver.Process("www.example.com"));
        Assert.AreEqual(SniFilterAction.None, resolver.Process(null));
        Assert.AreEqual(SniFilterAction.None, resolver.Process(""));
        Assert.AreEqual(SniFilterAction.None, resolver.Process("   "));
    }

    [TestMethod]
    public void Process_ExactMatch_ReturnsCorrectAction()
    {
        var domainFilter = new SniFilterPolicy {
            Blocks = ["block.com"],
            Excludes = ["exclude.com"],
            Includes = ["include.com"]
        };
        var resolver = new SniFilterResolver(domainFilter);

        Assert.AreEqual(SniFilterAction.Block, resolver.Process("block.com"));
        Assert.AreEqual(SniFilterAction.Exclude, resolver.Process("exclude.com"));
        Assert.AreEqual(SniFilterAction.Include, resolver.Process("include.com"));
    }

    [TestMethod]
    public void Process_CaseInsensitive_ReturnsCorrectAction()
    {
        var domainFilter = new SniFilterPolicy {
            Blocks = ["Block.COM"],
            Excludes = ["EXCLUDE.com"],
            Includes = ["InClUdE.CoM"]
        };
        var resolver = new SniFilterResolver(domainFilter);

        Assert.AreEqual(SniFilterAction.Block, resolver.Process("block.com"));
        Assert.AreEqual(SniFilterAction.Block, resolver.Process("BLOCK.COM"));
        Assert.AreEqual(SniFilterAction.Block, resolver.Process("BlOcK.cOm"));

        Assert.AreEqual(SniFilterAction.Exclude, resolver.Process("exclude.com"));
        Assert.AreEqual(SniFilterAction.Exclude, resolver.Process("EXCLUDE.COM"));

        Assert.AreEqual(SniFilterAction.Include, resolver.Process("include.com"));
        Assert.AreEqual(SniFilterAction.Include, resolver.Process("INCLUDE.COM"));
    }

    [TestMethod]
    public void Process_WildcardDomain_MatchesSubdomains()
    {
        var domainFilter = new SniFilterPolicy {
            Blocks = ["*.block.com"],
            Excludes = ["*.exclude.com"],
            Includes = ["*.include.com"]
        };
        var resolver = new SniFilterResolver(domainFilter);

        // Exact match (wildcard means *.domain.com includes domain.com itself)
        Assert.AreEqual(SniFilterAction.Block, resolver.Process("block.com"));
        Assert.AreEqual(SniFilterAction.Exclude, resolver.Process("exclude.com"));
        Assert.AreEqual(SniFilterAction.Include, resolver.Process("include.com"));

        // Subdomain match
        Assert.AreEqual(SniFilterAction.Block, resolver.Process("www.block.com"));
        Assert.AreEqual(SniFilterAction.Block, resolver.Process("api.block.com"));
        Assert.AreEqual(SniFilterAction.Block, resolver.Process("deep.sub.block.com"));

        Assert.AreEqual(SniFilterAction.Exclude, resolver.Process("www.exclude.com"));
        Assert.AreEqual(SniFilterAction.Exclude, resolver.Process("api.exclude.com"));

        Assert.AreEqual(SniFilterAction.Include, resolver.Process("www.include.com"));
        Assert.AreEqual(SniFilterAction.Include, resolver.Process("api.include.com"));
    }

    [TestMethod]
    public void Process_WildcardDomain_DoesNotMatchParentDomain()
    {
        var domainFilter = new SniFilterPolicy {
            Blocks = ["*.sub.example.com"]
        };
        var resolver = new SniFilterResolver(domainFilter);

        // Should match
        Assert.AreEqual(SniFilterAction.Block, resolver.Process("sub.example.com"));
        Assert.AreEqual(SniFilterAction.Block, resolver.Process("www.sub.example.com"));
        Assert.AreEqual(SniFilterAction.Block, resolver.Process("api.sub.example.com"));

        // Should NOT match parent domain
        Assert.AreEqual(SniFilterAction.None, resolver.Process("example.com"));
        Assert.AreEqual(SniFilterAction.None, resolver.Process("other.example.com"));
    }

    [TestMethod]
    public void Process_PriorityOrder_BlockBeforeExclude()
    {
        var domainFilter = new SniFilterPolicy {
            Blocks = ["example.com"],
            Excludes = ["example.com"]
        };
        var resolver = new SniFilterResolver(domainFilter);

        // Block takes priority
        Assert.AreEqual(SniFilterAction.Block, resolver.Process("example.com"));
    }

    [TestMethod]
    public void Process_PriorityOrder_ExcludeBeforeInclude()
    {
        var domainFilter = new SniFilterPolicy {
            Excludes = ["example.com"],
            Includes = ["example.com"]
        };
        var resolver = new SniFilterResolver(domainFilter);

        // Exclude takes priority
        Assert.AreEqual(SniFilterAction.Exclude, resolver.Process("example.com"));
    }

    [TestMethod]
    public void Process_PriorityOrder_BlockBeforeExcludeBeforeInclude()
    {
        var domainFilter = new SniFilterPolicy {
            Blocks = ["example.com"],
            Excludes = ["example.com"],
            Includes = ["example.com"]
        };
        var resolver = new SniFilterResolver(domainFilter);

        // Block takes priority over all
        Assert.AreEqual(SniFilterAction.Block, resolver.Process("example.com"));
    }

    [TestMethod]
    public void Process_WhitespaceHandling_TrimsAndProcesses()
    {
        var domainFilter = new SniFilterPolicy {
            Blocks = ["  block.com  "],
            Excludes = [" exclude.com"],
            Includes = ["include.com "]
        };
        var resolver = new SniFilterResolver(domainFilter);

        Assert.AreEqual(SniFilterAction.Block, resolver.Process("block.com"));
        Assert.AreEqual(SniFilterAction.Block, resolver.Process("  block.com  "));

        Assert.AreEqual(SniFilterAction.Exclude, resolver.Process("exclude.com"));
        Assert.AreEqual(SniFilterAction.Include, resolver.Process("include.com"));
    }

    [TestMethod]
    public void Process_MultipleWildcards_MatchesCorrectly()
    {
        var domainFilter = new SniFilterPolicy {
            Blocks = ["*.google.com", "*.facebook.com", "*.twitter.com"]
        };
        var resolver = new SniFilterResolver(domainFilter);

        Assert.AreEqual(SniFilterAction.Block, resolver.Process("google.com"));
        Assert.AreEqual(SniFilterAction.Block, resolver.Process("www.google.com"));
        Assert.AreEqual(SniFilterAction.Block, resolver.Process("mail.google.com"));

        Assert.AreEqual(SniFilterAction.Block, resolver.Process("facebook.com"));
        Assert.AreEqual(SniFilterAction.Block, resolver.Process("api.facebook.com"));

        Assert.AreEqual(SniFilterAction.Block, resolver.Process("twitter.com"));
        Assert.AreEqual(SniFilterAction.Block, resolver.Process("api.twitter.com"));

        Assert.AreEqual(SniFilterAction.None, resolver.Process("example.com"));
    }

    [TestMethod]
    public void DomainFilter_Setter_UpdatesInternalArrays()
    {
        var domainFilter1 = new SniFilterPolicy {
            Blocks = ["block1.com"]
        };
        var resolver = new SniFilterResolver(domainFilter1);

        Assert.AreEqual(SniFilterAction.Block, resolver.Process("block1.com"));
        Assert.AreEqual(SniFilterAction.None, resolver.Process("block2.com"));

        // Update the domain filter
        var domainFilter2 = new SniFilterPolicy {
            Blocks = ["block2.com"]
        };
        resolver.SniFilterPolicy = domainFilter2;

        Assert.AreEqual(SniFilterAction.None, resolver.Process("block1.com"));
        Assert.AreEqual(SniFilterAction.Block, resolver.Process("block2.com"));
    }

    [TestMethod]
    public void Process_ComplexScenario_ReturnsCorrectActions()
    {
        var domainFilter = new SniFilterPolicy {
            Blocks = ["*.ads.com", "tracker.example.com"],
            Excludes = ["*.internal.company.com", "localhost"],
            Includes = ["*.cdn.cloudflare.com", "api.service.com"]
        };
        var resolver = new SniFilterResolver(domainFilter);

        // Blocks
        Assert.AreEqual(SniFilterAction.Block, resolver.Process("ads.com"));
        Assert.AreEqual(SniFilterAction.Block, resolver.Process("www.ads.com"));
        Assert.AreEqual(SniFilterAction.Block, resolver.Process("tracker.example.com"));

        // Excludes
        Assert.AreEqual(SniFilterAction.Exclude, resolver.Process("internal.company.com"));
        Assert.AreEqual(SniFilterAction.Exclude, resolver.Process("app.internal.company.com"));
        Assert.AreEqual(SniFilterAction.Exclude, resolver.Process("localhost"));

        // Includes
        Assert.AreEqual(SniFilterAction.Include, resolver.Process("cdn.cloudflare.com"));
        Assert.AreEqual(SniFilterAction.Include, resolver.Process("static.cdn.cloudflare.com"));
        Assert.AreEqual(SniFilterAction.Include, resolver.Process("api.service.com"));

        // None
        Assert.AreEqual(SniFilterAction.None, resolver.Process("example.com"));
        Assert.AreEqual(SniFilterAction.None, resolver.Process("www.example.com"));
    }

    [TestMethod]
    public void Process_DeepSubdomains_MatchesWildcard()
    {
        var domainFilter = new SniFilterPolicy {
            Blocks = ["*.example.com"]
        };
        var resolver = new SniFilterResolver(domainFilter);

        Assert.AreEqual(SniFilterAction.Block, resolver.Process("example.com"));
        Assert.AreEqual(SniFilterAction.Block, resolver.Process("www.example.com"));
        Assert.AreEqual(SniFilterAction.Block, resolver.Process("api.www.example.com"));
        Assert.AreEqual(SniFilterAction.Block, resolver.Process("deep.sub.domain.example.com"));
        Assert.AreEqual(SniFilterAction.Block, resolver.Process("very.deep.sub.domain.example.com"));
    }

    [TestMethod]
    public void Process_SpecificAndWildcard_BothWork()
    {
        var domainFilter = new SniFilterPolicy {
            Blocks = ["specific.example.com", "*.wildcard.com"]
        };
        var resolver = new SniFilterResolver(domainFilter);

        // Specific match
        Assert.AreEqual(SniFilterAction.Block, resolver.Process("specific.example.com"));
        Assert.AreEqual(SniFilterAction.None, resolver.Process("other.example.com"));

        // Wildcard match
        Assert.AreEqual(SniFilterAction.Block, resolver.Process("wildcard.com"));
        Assert.AreEqual(SniFilterAction.Block, resolver.Process("www.wildcard.com"));
        Assert.AreEqual(SniFilterAction.Block, resolver.Process("any.wildcard.com"));
    }

    [TestMethod]
    public void Process_EmptyStringInArray_IgnoredCorrectly()
    {
        var domainFilter = new SniFilterPolicy {
            Blocks = ["", "  ", "block.com", ""]
        };
        var resolver = new SniFilterResolver(domainFilter);

        Assert.AreEqual(SniFilterAction.Block, resolver.Process("block.com"));
        Assert.AreEqual(SniFilterAction.None, resolver.Process("example.com"));
    }

    [TestMethod]
    public void Process_NullAndEmptyInput_ReturnsNone()
    {
        var domainFilter = new SniFilterPolicy {
            Blocks = ["block.com"]
        };
        var resolver = new SniFilterResolver(domainFilter);

        Assert.AreEqual(SniFilterAction.None, resolver.Process(null));
        Assert.AreEqual(SniFilterAction.None, resolver.Process(""));
        Assert.AreEqual(SniFilterAction.None, resolver.Process("   "));
        Assert.AreEqual(SniFilterAction.None, resolver.Process("\t"));
    }

    [TestMethod]
    public void Process_SingleLabelDomain_WorksCorrectly()
    {
        var domainFilter = new SniFilterPolicy {
            Blocks = ["localhost", "*.local"]
        };
        var resolver = new SniFilterResolver(domainFilter);

        Assert.AreEqual(SniFilterAction.Block, resolver.Process("localhost"));
        Assert.AreEqual(SniFilterAction.Block, resolver.Process("LOCALHOST"));
        Assert.AreEqual(SniFilterAction.Block, resolver.Process("local"));
        Assert.AreEqual(SniFilterAction.Block, resolver.Process("my-server.local"));
    }
}
