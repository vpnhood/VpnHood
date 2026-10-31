using VpnHood.Net.Http3;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class Http3TrailerScheduleSamplerTest
{
    [TestMethod]
    [DataRow(0.80, 1L)]
    [DataRow(0.27, 8L)]
    [DataRow(0.05, 51L)]
    [DataRow(0.03, 151L)]
    public void Selection_survival_passes_through_each_anchor(double survival, long fields)
    {
        var random = new ReplayRandom([survival, 0, survival + 1e-10, 0, survival - 1e-10, 0]);
        var sampler = new Http3TrailerScheduleSampler(random);

        Assert.AreEqual(fields, sampler.Sample().SelectionFields);
        Assert.IsTrue(sampler.Sample().SelectionFields < fields);
        Assert.IsTrue(sampler.Sample().SelectionFields >= fields);
        Assert.AreEqual(6, random.DrawsRead);
    }

    [TestMethod]
    [DataRow(0.80, 0.27, 2L)]
    [DataRow(0.27, 0.05, 20L)]
    [DataRow(0.05, 0.03, 87L)]
    public void Selection_interpolates_in_log_log_space(double startSurvival, double endSurvival, long fields)
    {
        var sampler = new Http3TrailerScheduleSampler(new ReplayRandom([Math.Sqrt(startSurvival * endSurvival), 0]));

        Assert.AreEqual(fields, sampler.Sample().SelectionFields);
    }

    [TestMethod]
    public void Selection_extrapolates_the_last_slope()
    {
        var slope = Math.Log(0.03 / 0.05) / Math.Log(151.0 / 51);
        var survival = 0.03 * Math.Pow(302.5 / 151, slope);
        var sampler = new Http3TrailerScheduleSampler(new ReplayRandom([survival, 0]));

        Assert.AreEqual(302L, sampler.Sample().SelectionFields);
    }

    [TestMethod]
    public void Fixed_uniform_grid_has_the_calibrated_survival_shares()
    {
        const int samples = 10000;
        var draws = new double[samples * 2];
        for (var i = 0; i < samples; i++) {
            draws[i * 2] = (i + 0.5) / samples;
            draws[i * 2 + 1] = 0.5;
        }
        var random = new ReplayRandom(draws);
        var sampler = new Http3TrailerScheduleSampler(random);
        var atLeastOne = 0;
        var atLeastEight = 0;
        var atLeastFiftyOne = 0;
        var atLeastOneFiftyOne = 0;

        for (var i = 0; i < samples; i++) {
            var fields = sampler.Sample().SelectionFields;
            if (fields >= 1) atLeastOne++;
            if (fields >= 8) atLeastEight++;
            if (fields >= 51) atLeastFiftyOne++;
            if (fields >= 151) atLeastOneFiftyOne++;
        }

        Assert.AreEqual(8000, atLeastOne);
        Assert.AreEqual(2700, atLeastEight);
        Assert.AreEqual(500, atLeastFiftyOne);
        Assert.AreEqual(300, atLeastOneFiftyOne);
        Assert.AreEqual(samples * 2, random.DrawsRead);
    }

    [TestMethod]
    public void Selection_and_emission_use_independent_draws()
    {
        var random = new ReplayRandom([0.9, 1 - 0.8 * Math.Exp(-2), 0.03, 0]);
        var sampler = new Http3TrailerScheduleSampler(random);

        var first = sampler.Sample();
        var second = sampler.Sample();

        Assert.AreEqual(0L, first.SelectionFields);
        Assert.AreEqual(760.0, first.ApplicationEmissionBytes, 1e-9);
        Assert.AreEqual(20000.0, first.LimitEmissionBytes, 1e-9);
        Assert.AreEqual(151L, second.SelectionFields);
        Assert.AreEqual(0.0, second.ApplicationEmissionBytes);
        Assert.AreEqual(0.0, second.LimitEmissionBytes);
        Assert.AreEqual(4, random.DrawsRead);
    }

    [TestMethod]
    [DataRow(0.0)]
    [DataRow(0.1)]
    [DataRow(0.2)]
    public void Immediate_emission_atom_applies_to_both_responses(double draw)
    {
        var sampler = new Http3TrailerScheduleSampler(new ReplayRandom([0.9, draw]));

        var schedule = sampler.Sample();

        Assert.AreEqual(0.0, schedule.ApplicationEmissionBytes);
        Assert.AreEqual(0.0, schedule.LimitEmissionBytes);
    }

    [TestMethod]
    public void Exponential_tail_starts_just_after_the_atom()
    {
        var sampler = new Http3TrailerScheduleSampler(new ReplayRandom([0.9, 0.2 + 1e-10]));

        var schedule = sampler.Sample();

        Assert.IsGreaterThan(0.0, schedule.ApplicationEmissionBytes);
        Assert.IsGreaterThan(schedule.ApplicationEmissionBytes, schedule.LimitEmissionBytes);
        Assert.IsLessThan(1e-5, schedule.LimitEmissionBytes);
    }

    [TestMethod]
    public void Fixed_uniform_grid_has_the_atom_and_both_conditional_exponential_means()
    {
        const int samples = 10000;
        var draws = new double[samples * 2];
        for (var i = 0; i < samples; i++) {
            draws[i * 2] = 0.9;
            draws[i * 2 + 1] = (i + 0.5) / samples;
        }
        var random = new ReplayRandom(draws);
        var sampler = new Http3TrailerScheduleSampler(random);
        var immediate = 0;
        var applicationTotal = 0.0;
        var limitTotal = 0.0;
        var applicationPastMean = 0;
        var limitPastMean = 0;

        for (var i = 0; i < samples; i++) {
            var schedule = sampler.Sample();
            if (schedule.ApplicationEmissionBytes == 0) {
                Assert.AreEqual(0.0, schedule.LimitEmissionBytes);
                immediate++;
            }
            applicationTotal += schedule.ApplicationEmissionBytes;
            limitTotal += schedule.LimitEmissionBytes;
            if (schedule.ApplicationEmissionBytes >= 380) applicationPastMean++;
            if (schedule.LimitEmissionBytes >= 10000) limitPastMean++;
        }

        Assert.AreEqual(2000, immediate);
        Assert.AreEqual(380.0, applicationTotal / (samples - immediate), 0.1);
        Assert.AreEqual(10000.0, limitTotal / (samples - immediate), 1.0);
        Assert.AreEqual(2943, applicationPastMean);
        Assert.AreEqual(applicationPastMean, limitPastMean);
        Assert.AreEqual(samples * 2, random.DrawsRead);
    }

    [TestMethod]
    public void Endpoint_draws_keep_unbounded_schedules_representable()
    {
        var sampler = new Http3TrailerScheduleSampler(new ReplayRandom([0, 1, double.Epsilon, 0, 1,
            1 - 0.8 * Math.Exp(-1)]));

        var unbounded = sampler.Sample();
        var tiny = sampler.Sample();
        var early = sampler.Sample();

        Assert.AreEqual(long.MaxValue, unbounded.SelectionFields);
        Assert.IsTrue(double.IsPositiveInfinity(unbounded.ApplicationEmissionBytes));
        Assert.IsTrue(double.IsPositiveInfinity(unbounded.LimitEmissionBytes));
        Assert.AreEqual(long.MaxValue, tiny.SelectionFields);
        Assert.AreEqual(0.0, tiny.ApplicationEmissionBytes);
        Assert.AreEqual(0.0, tiny.LimitEmissionBytes);
        Assert.AreEqual(0L, early.SelectionFields);
        Assert.AreEqual(380.0, early.ApplicationEmissionBytes, 1e-9);
        Assert.AreEqual(10000.0, early.LimitEmissionBytes, 1e-9);
    }

    [TestMethod]
    [DataRow(-0.01)]
    [DataRow(1.01)]
    [DataRow(double.NaN)]
    public void Invalid_draws_are_rejected(double draw)
    {
        var sampler = new Http3TrailerScheduleSampler(new ReplayRandom([draw]));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => sampler.Sample());
    }

    [TestMethod]
    [DataRow(-0.01)]
    [DataRow(1.01)]
    [DataRow(double.NaN)]
    public void Invalid_emission_draws_are_rejected(double draw)
    {
        var sampler = new Http3TrailerScheduleSampler(new ReplayRandom([0.9, draw]));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => sampler.Sample());
    }
}
