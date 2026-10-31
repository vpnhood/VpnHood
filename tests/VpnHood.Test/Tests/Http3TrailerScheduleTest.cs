using VpnHood.Net.Http3;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class Http3TrailerScheduleTest
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    public async Task Immediate_emission_ignores_later_hard_overruns()
    {
        using var source = new MemoryStream(CreateRequest(CreateTrailerBlock(5)));
        var result = await Observe(source, new Http3ServerOptions { MaxHeaderCount = 4 },
            new Http3TrailerSchedule(0, 0, double.PositiveInfinity), TestContext.CancellationToken);

        Assert.AreEqual("401", result.Status);
        Assert.IsNull(result.ErrorCode);
        Assert.AreEqual(1, result.CompletedWrites);
        Assert.AreEqual(1, result.ScheduleSamples);
    }

    [TestMethod]
    public async Task Immediate_emission_at_the_last_trailer_precedes_the_following_frame()
    {
        byte[] request = [.. CreateRequest(CreateTrailerBlock(8)), .. Http3Protocol.Frame(0, [1])];
        using var source = new MemoryStream(request);
        var result = await Observe(source, new Http3ServerOptions(),
            new Http3TrailerSchedule(8, 0, double.PositiveInfinity), TestContext.CancellationToken);

        Assert.AreEqual("401", result.Status);
        Assert.IsNull(result.ErrorCode);
        Assert.AreEqual(1, result.CompletedWrites);
    }

    [TestMethod]
    public async Task Selection_beyond_the_section_observes_its_soft_overrun()
    {
        using var source = new MemoryStream(CreateRequest(CreateTrailerBlock(3)));
        var result = await Observe(source, new Http3ServerOptions { MaxHeaderCount = 4 },
            new Http3TrailerSchedule(long.MaxValue, double.PositiveInfinity, double.PositiveInfinity),
            TestContext.CancellationToken);

        Assert.AreEqual("431", result.Status);
        Assert.IsNull(result.ErrorCode);
        Assert.AreEqual(1, result.CompletedWrites);
    }

    [TestMethod]
    [DataRow(false, 0x10eL)]
    [DataRow(true, 0x10bL)]
    public async Task A_hard_overrun_before_selection_resets_without_response(bool octets, long errorCode)
    {
        byte[] trailers = octets ? [0, 0, 0x21, (byte)'x', 52, .. Enumerable.Repeat((byte)'a', 52)] :
            CreateTrailerBlock(5);
        var options = octets ? new Http3ServerOptions { MaxHeaderBytes = 50 } :
            new Http3ServerOptions { MaxHeaderCount = 4 };
        using var source = new MemoryStream(CreateRequest(trailers));
        var result = await Observe(source, options, new Http3TrailerSchedule(long.MaxValue, 0, 0),
            TestContext.CancellationToken);

        Assert.IsNull(result.Status);
        Assert.AreEqual(errorCode, result.ErrorCode);
        Assert.AreEqual(0, result.CompletedWrites);
    }

    [TestMethod]
    public async Task Selection_is_frozen_while_later_fields_cross_the_soft_limit()
    {
        using var source = new MemoryStream(CreateRequest(CreateTrailerBlock(5)));
        var result = await Observe(source, new Http3ServerOptions { MaxHeaderCount = 4 },
            new Http3TrailerSchedule(0, 6, double.PositiveInfinity), TestContext.CancellationToken);

        Assert.AreEqual("401", result.Status);
        Assert.IsNull(result.ErrorCode);
        Assert.AreEqual(1, result.CompletedWrites);
    }

    [TestMethod]
    public async Task Selection_at_a_completed_field_observes_its_increment()
    {
        using var source = new MemoryStream(CreateRequest(CreateTrailerBlock(5)));
        var result = await Observe(source, new Http3ServerOptions { MaxHeaderCount = 4 },
            new Http3TrailerSchedule(1, double.PositiveInfinity, 0), TestContext.CancellationToken);

        Assert.AreEqual("431", result.Status);
        Assert.IsNull(result.ErrorCode);
        Assert.AreEqual(1, result.CompletedWrites);
    }

    [TestMethod]
    public async Task A_hard_overrun_after_selection_but_before_emission_still_resets()
    {
        using var source = new MemoryStream(CreateRequest(CreateTrailerBlock(5)));
        var result = await Observe(source, new Http3ServerOptions { MaxHeaderCount = 4 },
            new Http3TrailerSchedule(1, 0, 12), TestContext.CancellationToken);

        Assert.IsNull(result.Status);
        Assert.AreEqual(Http3Protocol.MessageError, result.ErrorCode);
        Assert.AreEqual(0, result.CompletedWrites);
    }

    [TestMethod]
    [DataRow(0L, "401", null)]
    [DataRow(1L, null, 0x10eL)]
    public async Task The_selected_status_chooses_its_own_emission_distance(
        long selectionFields, string? status, long? errorCode)
    {
        using var source = new MemoryStream(CreateRequest(CreateTrailerBlock(5)));
        var result = await Observe(source, new Http3ServerOptions { MaxHeaderCount = 4 },
            new Http3TrailerSchedule(selectionFields, 6, 12), TestContext.CancellationToken);

        Assert.AreEqual(status, result.Status);
        Assert.AreEqual(errorCode, result.ErrorCode);
        Assert.AreEqual(errorCode == null ? 1 : 0, result.CompletedWrites);
    }

    [TestMethod]
    [DataRow(-1.0, 0.0)]
    [DataRow(double.NaN, 0.0)]
    [DataRow(double.NegativeInfinity, 0.0)]
    [DataRow(0.0, -1.0)]
    [DataRow(0.0, double.NaN)]
    [DataRow(0.0, double.NegativeInfinity)]
    public void Either_status_requires_a_valid_emission_distance(double applicationBytes, double limitBytes)
    {
        using var output = new MemoryStream();
        var options = new Http3ServerOptions();
        var errors = new List<Exception>();
        using var dispatch = new Http3ResponseDispatch(output, new Http3RecordingQuicStream(),
            new Http3RecordingResponder(), new Http3Request([], null), new Http3HeaderBudget(options),
            options, errors.Add, TestContext.CancellationToken);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new Http3TrailerInspection(new Http3TrailerSchedule(0, applicationBytes, limitBytes), dispatch));
        Assert.IsEmpty(errors);
    }

    [TestMethod]
    [DataRow(14.5, "401", null)]
    [DataRow(15.0, null, 0x10eL)]
    public async Task Emission_inside_a_field_precedes_its_increment_but_the_exact_end_does_not(
        double emissionBytes, string? status, long? errorCode)
    {
        using var source = new MemoryStream(CreateRequest(CreateTrailerBlock(5)));
        var result = await Observe(source, new Http3ServerOptions { MaxHeaderCount = 4 },
            new Http3TrailerSchedule(0, emissionBytes, emissionBytes), TestContext.CancellationToken);

        Assert.AreEqual(status, result.Status);
        Assert.AreEqual(errorCode, result.ErrorCode);
        Assert.AreEqual(errorCode == null ? 1 : 0, result.CompletedWrites);
    }

    [TestMethod]
    [DataRow(54.5, "401", null)]
    [DataRow(55.0, null, 0x10bL)]
    public async Task Emission_limits_bulk_string_reads_before_the_field_budget_increment(
        double emissionBytes, string? status, long? errorCode)
    {
        byte[] trailers = [0, 0, 0x21, (byte)'x', 52, .. Enumerable.Repeat((byte)'a', 52)];
        using var source = new MemoryStream(CreateRequest(trailers));
        var result = await Observe(source, new Http3ServerOptions { MaxHeaderBytes = 50 },
            new Http3TrailerSchedule(0, emissionBytes, emissionBytes), TestContext.CancellationToken);

        Assert.AreEqual(status, result.Status);
        Assert.AreEqual(errorCode, result.ErrorCode);
        Assert.AreEqual(errorCode == null ? 1 : 0, result.CompletedWrites);
    }

    [TestMethod]
    [DataRow(0L, "401")]
    [DataRow(long.MaxValue, "431")]
    public async Task Pending_input_selects_or_emits_without_waiting(long selectionFields, string status)
    {
        var request = CreateRequest(CreateTrailerBlock(5));
        using var source = new Http3AvailablePrefixStream(request[..^9]);
        var result = await Observe(source, new Http3ServerOptions { MaxHeaderCount = 4 },
            new Http3TrailerSchedule(selectionFields, double.PositiveInfinity, double.PositiveInfinity),
            TestContext.CancellationToken);

        Assert.AreEqual(status, result.Status);
        Assert.IsNull(result.ErrorCode);
        Assert.AreEqual(1, result.CompletedWrites);
        Assert.AreEqual(1, source.PendingReadsStarted);
    }

    [TestMethod]
    [DataRow(0L, "401")]
    [DataRow(long.MaxValue, "431")]
    public async Task The_inspection_budget_selects_or_emits_at_its_boundary(long selectionFields, string status)
    {
        using var source = new MemoryStream(CreateRequest(CreateTrailerBlock(5)));
        var result = await Observe(source,
            new Http3ServerOptions { MaxHeaderCount = 4, MaxBodyInspectionBytes = 10 },
            new Http3TrailerSchedule(selectionFields, double.PositiveInfinity, double.PositiveInfinity),
            TestContext.CancellationToken);

        Assert.AreEqual(status, result.Status);
        Assert.IsNull(result.ErrorCode);
        Assert.AreEqual(1, result.CompletedWrites);
    }

    [TestMethod]
    public async Task Data_before_trailers_keeps_its_length_validation_before_the_schedule()
    {
        byte[] request = [.. CreateHeaders(zeroContentLength: true), .. Http3Protocol.Frame(0, [1]),
            .. Http3Protocol.Frame(1, CreateTrailerBlock(5))];
        using var source = new MemoryStream(request);
        var result = await Observe(source, new Http3ServerOptions(), new Http3TrailerSchedule(0, 0, 0),
            TestContext.CancellationToken);

        Assert.IsNull(result.Status);
        Assert.AreEqual(Http3Protocol.ProtocolError, result.ErrorCode);
        Assert.AreEqual(0, result.CompletedWrites);
        Assert.AreEqual(0, result.ScheduleSamples);
    }

    [TestMethod]
    public async Task A_request_without_readable_trailers_does_not_draw_a_schedule()
    {
        using var source = new Http3AvailablePrefixStream(CreateHeaders(zeroContentLength: false));
        var result = await Observe(source, new Http3ServerOptions(),
            new Http3TrailerSchedule(long.MaxValue, double.PositiveInfinity, double.PositiveInfinity),
            TestContext.CancellationToken);

        Assert.AreEqual("401", result.Status);
        Assert.IsNull(result.ErrorCode);
        Assert.AreEqual(1, result.CompletedWrites);
        Assert.AreEqual(0, result.ScheduleSamples);
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(1L)]
    public async Task Selection_beyond_the_section_keeps_inspecting_for_forbidden_frames(long type)
    {
        byte[] request = [.. CreateRequest(CreateTrailerBlock(1)), .. Http3Protocol.Frame(type, [1])];
        using var source = new MemoryStream(request);
        var result = await Observe(source, new Http3ServerOptions(),
            new Http3TrailerSchedule(long.MaxValue, 0, 0), TestContext.CancellationToken);

        Assert.IsNull(result.Status);
        Assert.AreEqual(Http3Protocol.FrameUnexpected, result.ErrorCode);
        Assert.AreEqual(true, result.ConnectionError);
        Assert.AreEqual(0, result.CompletedWrites);
        Assert.AreEqual(1, result.ScheduleSamples);
    }

    [TestMethod]
    [DataRow(0L, 3.0, "401", null)]
    [DataRow(0L, 4.5, "401", null)]
    [DataRow(0L, 5.0, null, 0x105L)]
    [DataRow(1L, 1.5, "401", null)]
    [DataRow(1L, 2.0, null, 0x105L)]
    public async Task Emission_distance_continues_through_the_next_frame_header(
        long selectionFields, double emissionBytes, string? status, long? errorCode)
    {
        byte[] request = [.. CreateRequest(CreateTrailerBlock(1)), .. Http3Protocol.Frame(0, [1])];
        using var source = new MemoryStream(request);
        var result = await Observe(source, new Http3ServerOptions(),
            new Http3TrailerSchedule(selectionFields, emissionBytes, emissionBytes), TestContext.CancellationToken);

        Assert.AreEqual(status, result.Status);
        Assert.AreEqual(errorCode, result.ErrorCode);
        Assert.AreEqual(errorCode == null ? null : (bool?)true, result.ConnectionError);
        Assert.AreEqual(errorCode == null ? 1 : 0, result.CompletedWrites);
    }

    [TestMethod]
    [DataRow(305.5, "401", null)]
    [DataRow(306.0, null, 0x105L)]
    public async Task Emission_counts_extension_varints_and_discarded_payload_after_trailers(
        double emissionBytes, string? status, long? errorCode)
    {
        byte[] request = [.. CreateRequest(CreateTrailerBlock(1)), .. Http3Protocol.Frame(64, new byte[300]),
            .. Http3Protocol.Frame(0, [1])];
        using var source = new MemoryStream(request);
        var result = await Observe(source, new Http3ServerOptions(),
            new Http3TrailerSchedule(1, emissionBytes, emissionBytes), TestContext.CancellationToken);

        Assert.AreEqual(status, result.Status);
        Assert.AreEqual(errorCode, result.ErrorCode);
        Assert.AreEqual(errorCode == null ? 1 : 0, result.CompletedWrites);
    }

    [TestMethod]
    [DataRow(0L, "401")]
    [DataRow(long.MaxValue, "431")]
    public async Task Pending_input_after_trailers_ends_selection_or_emission(long selectionFields, string status)
    {
        using var source = new Http3AvailablePrefixStream(CreateRequest(CreateTrailerBlock(3)));
        var result = await Observe(source, new Http3ServerOptions { MaxHeaderCount = 4 },
            new Http3TrailerSchedule(selectionFields, double.PositiveInfinity, double.PositiveInfinity),
            TestContext.CancellationToken);

        Assert.AreEqual(status, result.Status);
        Assert.IsNull(result.ErrorCode);
        Assert.AreEqual(1, result.CompletedWrites);
        Assert.AreEqual(1, source.PendingReadsStarted);
    }

    [TestMethod]
    [DataRow(0L, "401")]
    [DataRow(long.MaxValue, "431")]
    public async Task The_inspection_budget_still_bounds_input_after_trailers(long selectionFields, string status)
    {
        byte[] request = [.. CreateRequest(CreateTrailerBlock(1)), .. Http3Protocol.Frame(64, new byte[300]),
            .. Http3Protocol.Frame(0, [1])];
        using var source = new MemoryStream(request);
        var result = await Observe(source,
            new Http3ServerOptions { MaxHeaderCount = 4, MaxBodyInspectionBytes = 20 },
            new Http3TrailerSchedule(selectionFields, double.PositiveInfinity, double.PositiveInfinity),
            TestContext.CancellationToken);

        Assert.AreEqual(status, result.Status);
        Assert.IsNull(result.ErrorCode);
        Assert.AreEqual(1, result.CompletedWrites);
    }

    private static async Task<(string? Status, long? ErrorCode, int CompletedWrites, int ScheduleSamples,
        bool? ConnectionError)> Observe(
        Stream source, Http3ServerOptions options, Http3TrailerSchedule schedule, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        using var output = new MemoryStream();
        await using var reader = new Http3StreamReader(source);
        var quic = new Http3RecordingQuicStream();
        var errors = new List<Exception>();
        var sampler = new PinnedHttp3TrailerScheduleSampler(schedule);
        var failure = await Process(output, reader, quic, errors.Add, options, sampler, deadline.Token);
        if (failure != null)
            Assert.AreSame(failure, errors.Single());
        else
            Assert.IsEmpty(errors);
        var status = output.Length == 0 ? null : await ReadStatus(output.ToArray(), deadline.Token);
        return (status, failure?.ErrorCode, quic.CompletedWrites, sampler.Samples, failure?.ConnectionError);
    }

    private static async Task<Http3ProtocolException?> Process(Stream output, Http3StreamReader reader,
        Http3RecordingQuicStream quic, Action<Exception> onError, Http3ServerOptions options,
        IHttp3TrailerScheduleSampler trailerScheduleSampler, CancellationToken cancellationToken)
    {
        try {
            await Http3RequestStream.Process(output, reader, quic, onError, new Http3RecordingResponder(),
                options, trailerScheduleSampler, cancellationToken, cancellationToken);
            return null;
        }
        catch (Http3ProtocolException ex) {
            return ex;
        }
    }

    private static async Task<string> ReadStatus(byte[] response, CancellationToken cancellationToken)
    {
        using var source = new MemoryStream(response);
        await using var reader = new Http3StreamReader(source);
        Assert.AreEqual(1L, await reader.ReadInteger(cancellationToken));
        var length = await reader.ReadInteger(cancellationToken);
        var section = await Http3Qpack.ReadSection(reader, length, new Http3ServerOptions(), cancellationToken);
        return section.Fields.Single(field => field.Key == ":status").Value;
    }

    private static byte[] CreateRequest(IReadOnlyList<byte> trailers) =>
        [.. CreateHeaders(zeroContentLength: false), .. Http3Protocol.Frame(1, trailers.ToArray())];

    private static byte[] CreateHeaders(bool zeroContentLength)
    {
        byte[] fields = [0, 0, 0xd1, 0xd7, 0xc1, 0x50, 11, .. "example.com"u8];
        byte[] block = zeroContentLength ? [.. fields, 0xc4] : fields;
        return Http3Protocol.Frame(1, block);
    }

    private static byte[] CreateTrailerBlock(int fields)
    {
        var bytes = new List<byte> { 0, 0 };
        for (var i = 0; i < fields; i++)
            bytes.AddRange([0x21, (byte)'x', 0]);
        return bytes.ToArray();
    }
}
