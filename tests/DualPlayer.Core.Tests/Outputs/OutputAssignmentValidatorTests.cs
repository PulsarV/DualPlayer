using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;

namespace DualPlayer.Core.Tests.Outputs;

public sealed class OutputAssignmentValidatorTests
{
    private const string IdA = "{0.0.0.00000000}.{aaaa}";
    private const string IdB = "{0.0.0.00000000}.{bbbb}";
    private const string IdC = "{0.0.0.00000000}.{cccc}";

    private static readonly IReadOnlyList<AudioTrackInfo> Tracks =
    [
        new(1, "eng", ChannelLayout.Stereo, 48000),
        new(2, "ukr", ChannelLayout.Stereo, 48000),
    ];

    // Deliberately unsorted: the catalog must sort by OS endpoint id and number from 1.
    private static DeviceCatalog Catalog() => new(
    [
        new AudioDeviceInfo("Speakers C", null, IdC),
        new AudioDeviceInfo("Headset A", "bluetooth", IdA),
        new AudioDeviceInfo("Headset B", "bluetooth", IdB),
    ]);

    [Fact]
    public void Catalog_numbers_devices_from_1_sorted_by_endpoint_id()
    {
        var devices = Catalog().Devices;

        devices.Select(d => d.Id).ToArray().ShouldBe([IdA, IdB, IdC]);
    }

    [Fact]
    public void Device_given_by_list_number_resolves_to_its_full_os_id()
    {
        var result = OutputAssignmentValidator.Validate(
            Tracks,
            Catalog(),
            [new OutputAssignment(1, DeviceRef.ByNumber(2)), new OutputAssignment(2, DeviceRef.ById(IdC))]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(o => (o.TrackNumber, o.DeviceId)).ToArray().ShouldBe([(1, IdB), (2, IdC)]);
    }

    [Fact]
    public void Track_above_the_track_count_is_refused_listing_existing_tracks()
    {
        var result = OutputAssignmentValidator.Validate(Tracks, Catalog(), [new OutputAssignment(3, DeviceRef.ByNumber(1))]);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("playback.track_not_found");
        result.Error.Message.ShouldContain("track 3 does not exist");
        result.Error.Message.ShouldContain("the file has tracks 1 and 2");
    }

    [Fact]
    public void Track_number_zero_is_refused_as_track_not_found()
    {
        var result = OutputAssignmentValidator.Validate(Tracks, Catalog(), [new OutputAssignment(0, DeviceRef.ByNumber(1))]);

        result.Error!.Code.ShouldBe("playback.track_not_found");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void Device_number_out_of_range_is_refused_as_device_not_found(int number)
    {
        var result = OutputAssignmentValidator.Validate(Tracks, Catalog(), [new OutputAssignment(1, DeviceRef.ByNumber(number))]);

        result.Error!.Code.ShouldBe("outputs.device_not_found");
    }

    [Fact]
    public void Unknown_device_id_is_refused_as_device_not_found()
    {
        var result = OutputAssignmentValidator.Validate(Tracks, Catalog(), [new OutputAssignment(1, DeviceRef.ById("nope"))]);

        result.Error!.Code.ShouldBe("outputs.device_not_found");
        result.Error.Message.ShouldContain("nope");
    }

    [Fact]
    public void Same_device_on_two_outputs_is_refused_naming_the_rule()
    {
        var result = OutputAssignmentValidator.Validate(
            Tracks,
            Catalog(),
            [new OutputAssignment(1, DeviceRef.ById(IdA)), new OutputAssignment(2, DeviceRef.ById(IdA))]);

        result.Error!.Code.ShouldBe("outputs.device_assigned_twice");
        result.Error.Message.ShouldContain("one audio output per device");
    }

    [Fact]
    public void Same_device_given_once_by_number_and_once_by_id_is_refused_as_assigned_twice()
    {
        var result = OutputAssignmentValidator.Validate(
            Tracks,
            Catalog(),
            [new OutputAssignment(1, DeviceRef.ByNumber(1)), new OutputAssignment(2, DeviceRef.ById(IdA))]);

        result.Error!.Code.ShouldBe("outputs.device_assigned_twice");
    }

    [Fact]
    public void Same_track_on_two_different_devices_is_valid()
    {
        var result = OutputAssignmentValidator.Validate(
            Tracks,
            Catalog(),
            [new OutputAssignment(1, DeviceRef.ByNumber(1)), new OutputAssignment(1, DeviceRef.ByNumber(2))]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(o => o.DeviceId).ToArray().ShouldBe([IdA, IdB]);
    }

    [Fact]
    public void First_invalid_input_wins()
    {
        var result = OutputAssignmentValidator.Validate(
            Tracks,
            Catalog(),
            [new OutputAssignment(3, DeviceRef.ByNumber(1)), new OutputAssignment(1, DeviceRef.ByNumber(9))]);

        result.Error!.Code.ShouldBe("playback.track_not_found");
    }
}
