using TrayRandom.Text;

namespace TrayRandom.Tests;

[TestClass]
public sealed class TextScrambleTests
{
    [TestMethod]
    public void Frame_FullProgress_ReturnsTarget() =>
        Assert.AreEqual("Hello, World 42", TextScramble.Frame("Hello, World 42", 1, new Random(1)));

    [TestMethod]
    public void Frame_NoProgress_KeepsShape()
    {
        const string target = "Ab1, x\ny.";

        string frame = TextScramble.Frame(target, 0, new Random(3));

        Assert.AreEqual(target.Length, frame.Length);
        for (int i = 0; i < target.Length; i++)
        {
            if (char.IsAsciiLetterUpper(target[i]))
            {
                Assert.IsTrue(char.IsAsciiLetterUpper(frame[i]));
            }
            else if (char.IsAsciiLetterLower(target[i]))
            {
                Assert.IsTrue(char.IsAsciiLetterLower(frame[i]));
            }
            else if (char.IsAsciiDigit(target[i]))
            {
                Assert.IsTrue(char.IsAsciiDigit(frame[i]));
            }
            else
            {
                Assert.AreEqual(target[i], frame[i]);
            }
        }
    }

    [TestMethod]
    public void Frame_HalfProgress_SettlesLeadingHalf()
    {
        const string target = "abcdefghijklmnopqrst";

        string frame = TextScramble.Frame(target, 0.5, new Random(9));

        Assert.AreEqual(target[..10], frame[..10]);
    }

    [TestMethod]
    public void Frame_EmptyText_ReturnsEmpty() =>
        Assert.AreEqual(string.Empty, TextScramble.Frame(string.Empty, 0.3, new Random(1)));
}
