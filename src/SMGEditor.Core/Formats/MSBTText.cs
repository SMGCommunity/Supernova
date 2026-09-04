using System.Buffers.Binary;
using System.Text;

namespace SMGEditor.Core.Formats;

public static class MSBTText
{
    private static readonly string[] FontColors = ["black", "red", "green", "blue", "yellow", "purple", "orange", "grey"];
    private static readonly string[] FontSizes = ["small", "normal", "large"];
    private static readonly string[] RaceTimes = ["jungle_glider", "challenge_glider", "reserved2", "reserved3", "reserved4", "last"];

    private static readonly int[] PictureCodes = [.. Enumerable.Range(0, 44), .. Enumerable.Range(49, 78 - 49)];

    private static readonly string[] PictureNames =
    [
        "a_button", "b_button", "c_button", "wiimote", "nunchuck", "1_button", "2_button", "star",
        "launch_star", "pull_star", "pointer", "purple_starbit", "coconut", "orange_arrow", "star_bunny",
        "analog_stick", "x_mark", "coin", "mario", "dpad", "blue_chip", "star_chip", "home_button",
        "minus_button", "plus_button", "z_button", "silver_star", "grand_star", "luigi", "co_pointer",
        "purple_coin", "green_comet", "gold_crown", "cross_hair", "blank", "bowser", "hand_grab",
        "hand_point", "hand_hold", "rainbow_starbit", "peach", "letter", "white_qmark", "current_player",
        "1up_mushroom", "life_mushroom", "hungry_luma", "luma", "comet", "green_qmark", "stopwatch",
        "master_luma", "yoshi", "comet_medal", "silver_crown", "yoshi_grapple", "checkpoint_flag",
        "empty_star", "empty_comet_medal", "empty_comet", "empty_secret_star", "bronze_star",
        "blimp_fruit", "platinum_crown", "bronze_grand_star", "topman", "goomba", "coins", "dpad_up",
        "dpad_down", "orange_luma", "toad", "bronze_comet",
    ];

    public static string ToEditableText(IReadOnlyList<MSBTTextRun> parts)
    {
        var sb = new StringBuilder();
        foreach (MSBTTextRun part in parts)
        {
            switch (part)
            {
                case MSBTTextRun.Literal literal:
                    sb.Append(literal.Value.Replace("[", "[["));
                    break;
                case MSBTTextRun.Tag tag:
                    sb.Append(DescribeTag(tag));
                    break;
            }
        }

        return sb.ToString();
    }

    public static List<MSBTTextRun> ParseMessageText(string text)
    {
        var parts = new List<MSBTTextRun>();
        var literal = new StringBuilder();
        void FlushLiteral()
        {
            if (literal.Length > 0)
            {
                parts.Add(new MSBTTextRun.Literal(literal.ToString()));
                literal.Clear();
            }
        }

        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '[' && i + 1 < text.Length && text[i + 1] == '[')
            {
                literal.Append('[');
                i += 2;
            }
            else if (text[i] == '[')
            {
                int end = text.IndexOf(']', i + 1);
                if (end < 0)
                {
                    throw new FormatException($"Unterminated '[' at position {i}.");
                }

                FlushLiteral();
                parts.Add(ParseTag(text[(i + 1)..end]));
                i = end + 1;
            }
            else
            {
                literal.Append(text[i]);
                i++;
            }
        }

        FlushLiteral();
        return parts;
    }

    public static string DescribeTag(MSBTTextRun.Tag tag)
    {
        byte[] p = tag.Payload;

        switch (tag.Group, tag.TagId)
        {
            case (0, 0) when p.Length >= 4:
            {
                ushort lenKanji = BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(0, 2));
                ushort lenFurigana = BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(2, 2));
                string furigana = Encoding.BigEndianUnicode.GetString(p, 4, lenFurigana);
                string kanji = Encoding.BigEndianUnicode.GetString(p, 4 + lenFurigana, lenKanji);
                return $"[ruby:{kanji};{furigana}]";
            }
            case (0, 3) when p.Length == 2:
            {
                ushort colorId = BinaryPrimitives.ReadUInt16BigEndian(p);
                return colorId == 0xFFFF ? "[defcolor]"
                    : colorId < FontColors.Length ? $"[color:{FontColors[colorId]}]"
                    : RawTag(tag);
            }
            case (1, 0) when p.Length == 2:
                return $"[delay:{BinaryPrimitives.ReadUInt16BigEndian(p)}]";
            case (1, 1) when p.Length == 0:
                return "[pagebreak]";
            case (1, 2) when p.Length == 0:
                return "[ycenter]";
            case (1, 3) when p.Length == 0:
                return "[xcenter]";
            case (2, 0) when p.Length >= 2:
            {
                ushort lenSound = BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(0, 2));
                string soundName = Encoding.BigEndianUnicode.GetString(p, 2, lenSound);
                return $"[sound:{soundName}]";
            }
            case (3, _) when tag.TagId < PictureNames.Length:
                return $"[icon:{PictureNames[tag.TagId]}]";
            case (4, _) when tag.TagId < FontSizes.Length && p.Length == 0:
                return $"[size:{FontSizes[tag.TagId]}]";
            case (5, 0) when p.Length == 2:
                return $"[player:{p[0]}]";
            case (6, _) when p.Length == 8:
            {
                int defaultValue = BinaryPrimitives.ReadInt32BigEndian(p.AsSpan(0, 4));
                uint vaArgIdx = BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(4, 4));
                return $"[intvar:{tag.TagId};{vaArgIdx};{defaultValue}]";
            }
            case (7, _) when p.Length == 8:
            {
                uint unknownArg = BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(0, 4));
                uint vaArgIdx = BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(4, 4));
                return $"[stringvar:{tag.TagId};{vaArgIdx};0x{unknownArg:X8}]";
            }
            case (9, _) when tag.TagId < RaceTimes.Length && p.Length == 0:
                return $"[race:{RaceTimes[tag.TagId]}]";
            case (10, 0) when p.Length >= 2:
            {
                ushort lenText = BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(0, 2));
                string numberText = Encoding.BigEndianUnicode.GetString(p, 2, lenText);
                return $"[numberfont:{numberText}]";
            }
            default:
                return RawTag(tag);
        }
    }

    private static string RawTag(MSBTTextRun.Tag tag) => $"[{tag.Group}:{tag.TagId};{Convert.ToHexString(tag.Payload)}]";

    public static MSBTTextRun.Tag ParseTag(string directive)
    {
        int colon = directive.IndexOf(':');
        string name = colon < 0 ? directive : directive[..colon];
        string[] args = colon < 0 ? [] : directive[(colon + 1)..].Split(';');

        switch (name)
        {
            case "ruby":
            {
                RequireArgs(name, args, 2);
                byte[] kanji = Encoding.BigEndianUnicode.GetBytes(args[0]);
                byte[] furigana = Encoding.BigEndianUnicode.GetBytes(args[1]);
                byte[] payload = new byte[4 + furigana.Length + kanji.Length];
                BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(0, 2), (ushort)kanji.Length);
                BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2, 2), (ushort)furigana.Length);
                furigana.CopyTo(payload, 4);
                kanji.CopyTo(payload, 4 + furigana.Length);
                return new MSBTTextRun.Tag(0, 0, payload);
            }
            case "defcolor":
                RequireArgs(name, args, 0);
                return new MSBTTextRun.Tag(0, 3, ToBigEndian((ushort)0xFFFF));
            case "color":
            {
                RequireArgs(name, args, 1);
                int colorId = Array.IndexOf(FontColors, args[0]);
                if (colorId < 0)
                {
                    throw new FormatException($"Unknown color '{args[0]}'.");
                }

                return new MSBTTextRun.Tag(0, 3, ToBigEndian((ushort)colorId));
            }
            case "delay":
                RequireArgs(name, args, 1);
                return new MSBTTextRun.Tag(1, 0, ToBigEndian(ushort.Parse(args[0])));
            case "pagebreak":
                RequireArgs(name, args, 0);
                return new MSBTTextRun.Tag(1, 1, []);
            case "ycenter":
                RequireArgs(name, args, 0);
                return new MSBTTextRun.Tag(1, 2, []);
            case "xcenter":
                RequireArgs(name, args, 0);
                return new MSBTTextRun.Tag(1, 3, []);
            case "sound":
            {
                RequireArgs(name, args, 1);
                byte[] encoded = Encoding.BigEndianUnicode.GetBytes(args[0]);
                byte[] payload = new byte[2 + encoded.Length];
                BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(0, 2), (ushort)encoded.Length);
                encoded.CopyTo(payload, 2);
                return new MSBTTextRun.Tag(2, 0, payload);
            }
            case "icon":
            {
                RequireArgs(name, args, 1);
                int iconIndex = Array.IndexOf(PictureNames, args[0]);
                if (iconIndex < 0)
                {
                    throw new FormatException($"Unknown icon '{args[0]}'.");
                }

                return new MSBTTextRun.Tag(3, (ushort)iconIndex, ToBigEndian((ushort)PictureCodes[iconIndex]));
            }
            case "size":
            {
                RequireArgs(name, args, 1);
                int sizeIndex = Array.IndexOf(FontSizes, args[0]);
                if (sizeIndex < 0)
                {
                    throw new FormatException($"Unknown size '{args[0]}'.");
                }

                return new MSBTTextRun.Tag(4, (ushort)sizeIndex, []);
            }
            case "player":
                RequireArgs(name, args, 1);
                return new MSBTTextRun.Tag(5, 0, [byte.Parse(args[0]), 0xCD]);
            case "intvar":
            {
                RequireArgs(name, args, 3);
                ushort tagId = ushort.Parse(args[0]);
                uint vaArgIdx = uint.Parse(args[1]);
                int defaultValue = int.Parse(args[2]);
                byte[] payload = new byte[8];
                BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0, 4), defaultValue);
                BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(4, 4), vaArgIdx);
                return new MSBTTextRun.Tag(6, tagId, payload);
            }
            case "stringvar":
            {
                RequireArgs(name, args, 3);
                ushort tagId = ushort.Parse(args[0]);
                uint vaArgIdx = uint.Parse(args[1]);
                uint unknownArg = Convert.ToUInt32(args[2], 16);
                byte[] payload = new byte[8];
                BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(0, 4), unknownArg);
                BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(4, 4), vaArgIdx);
                return new MSBTTextRun.Tag(7, tagId, payload);
            }
            case "race":
            {
                RequireArgs(name, args, 1);
                int raceIndex = Array.IndexOf(RaceTimes, args[0]);
                if (raceIndex < 0)
                {
                    throw new FormatException($"Unknown race name '{args[0]}'.");
                }

                return new MSBTTextRun.Tag(9, (ushort)raceIndex, []);
            }
            case "numberfont":
            {
                RequireArgs(name, args, 1);
                byte[] encoded = Encoding.BigEndianUnicode.GetBytes(args[0]);
                byte[] payload = new byte[2 + encoded.Length];
                BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(0, 2), (ushort)encoded.Length);
                encoded.CopyTo(payload, 2);
                return new MSBTTextRun.Tag(10, 0, payload);
            }
            default:
            {
                if (colon < 0 || args.Length != 2 || !ushort.TryParse(name, out ushort group) || !ushort.TryParse(args[0], out ushort tagId))
                {
                    throw new FormatException($"Unrecognized tag directive '[{directive}]'.");
                }

                return new MSBTTextRun.Tag(group, tagId, Convert.FromHexString(args[1]));
            }
        }
    }

    private static void RequireArgs(string tagName, string[] args, int expected)
    {
        if (args.Length != expected)
        {
            throw new FormatException($"Tag '{tagName}' expects {expected} argument(s), found {args.Length}.");
        }
    }

    private static byte[] ToBigEndian(ushort value)
    {
        byte[] result = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(result, value);
        return result;
    }
}
