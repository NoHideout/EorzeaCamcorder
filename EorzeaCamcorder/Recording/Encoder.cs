using System.Linq;

namespace EorzeaCamcorder.Recording;

public enum EncoderType
{
    SoftwareH264,
    NvidiaH264,
    AmdH264,
    IntelH264
}

public record EncoderProfile(EncoderType Type, string FFmpegCodec, bool IsHardwareAccelerated);

public static class EncoderReg
{
    public static readonly EncoderProfile[] Profiles =
    {
        new(EncoderType.SoftwareH264, "libx264", false),
        new(EncoderType.NvidiaH264, "h264_nvenc", true),
        new(EncoderType.AmdH264, "h264_amf", true),
        new(EncoderType.IntelH264, "h264_qsv", true)
    };
    public static readonly string[] ProfileNames = Profiles.Select(p => p.Type.ToString()).ToArray();
    public static EncoderProfile GetProfile(EncoderType type)
    {
        foreach (var profile in Profiles)
        {
            if (profile.Type == type) return profile;
        }
        return Profiles[0];
    }
}
