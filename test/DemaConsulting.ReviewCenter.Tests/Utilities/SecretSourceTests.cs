// Copyright (c) DEMA Consulting
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.Text;
using DemaConsulting.ReviewCenter.Utilities;

namespace DemaConsulting.ReviewCenter.Tests;

/// <summary>
///     Unit tests for the SecretSource class. Every file lives in a <see cref="TemporaryDirectory"/>; nothing reads
///     the real environment or home directory.
/// </summary>
public class SecretSourceTests
{
    /// <summary>
    ///     A secret that must never appear in a message.
    /// </summary>
    private const string Secret = "s3cr3t-value-for-tests";

    /// <summary>
    ///     The name of the inline setting in the messages.
    /// </summary>
    private const string ValueSetting = "Test:Secret";

    /// <summary>
    ///     The name of the file setting in the messages.
    /// </summary>
    private const string FileSetting = "Test:SecretFile";

    /// <summary>
    ///     Writes a file with the given bytes and returns its path.
    /// </summary>
    /// <param name="directory">The directory of the test.</param>
    /// <param name="bytes">The contents of the file.</param>
    /// <returns>The path of the file.</returns>
    private static string WriteFile(TemporaryDirectory directory, byte[] bytes)
    {
        var path = directory.GetFilePath("secret.txt");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>
    ///     Writes a text file and returns its path.
    /// </summary>
    /// <param name="directory">The directory of the test.</param>
    /// <param name="text">The contents of the file.</param>
    /// <returns>The path of the file.</returns>
    private static string WriteFile(TemporaryDirectory directory, string text)
    {
        return WriteFile(directory, new UTF8Encoding(false).GetBytes(text));
    }

    /// <summary>
    ///     Test that an inline value is returned as it is, without trimming.
    /// </summary>
    [Fact]
    public void SecretSource_Resolve_InlineValue_ReturnsTheValueUnchanged()
    {
        // Arrange: an inline value with surrounding spaces, and no file

        // Act
        var secret = SecretSource.Resolve($" {Secret} ", ValueSetting, null, FileSetting);

        // Assert
        Assert.Equal($" {Secret} ", secret);
    }

    /// <summary>
    ///     Test that the contents of a file are the secret.
    /// </summary>
    [Fact]
    public void SecretSource_Resolve_File_ReturnsTheContents()
    {
        // Arrange: a file that holds the secret
        using var directory = new TemporaryDirectory();
        var path = WriteFile(directory, Secret);

        // Act
        var secret = SecretSource.Resolve(string.Empty, ValueSetting, path, FileSetting);

        // Assert
        Assert.Equal(Secret, secret);
    }

    /// <summary>
    ///     Test that surrounding white space and a byte order mark of a file are not part of the secret, and that
    ///     white space inside it is kept.
    /// </summary>
    /// <param name="text">The contents of the file.</param>
    /// <param name="expected">The expected secret.</param>
    [Theory]
    [InlineData("secret\n", "secret")]
    [InlineData("secret\r\n", "secret")]
    [InlineData("\n  secret \t\r\n\r\n", "secret")]
    [InlineData("\uFEFFsecret\n", "secret")]
    [InlineData("two words\n", "two words")]
    public void SecretSource_Resolve_FileWithSurroundingWhiteSpace_ReturnsTrimmedSecret(string text, string expected)
    {
        // Arrange
        using var directory = new TemporaryDirectory();
        var path = WriteFile(directory, text);

        // Act
        var secret = SecretSource.Resolve(null, ValueSetting, path, FileSetting);

        // Assert
        Assert.Equal(expected, secret);
    }

    /// <summary>
    ///     Test that a file at exactly the size limit is accepted.
    /// </summary>
    [Fact]
    public void SecretSource_Resolve_FileAtSizeLimit_ReturnsTheContents()
    {
        // Arrange: a file of exactly the largest accepted size
        using var directory = new TemporaryDirectory();
        var text = new string('k', SecretSource.MaxFileBytes);
        var path = WriteFile(directory, text);

        // Act
        var secret = SecretSource.Resolve(null, ValueSetting, path, FileSetting);

        // Assert
        Assert.Equal(text, secret);
    }

    /// <summary>
    ///     Test that neither setting set (null, empty or white space) gives no secret and no error, so the caller
    ///     decides whether it is required.
    /// </summary>
    /// <param name="value">The inline value.</param>
    /// <param name="file">The file path.</param>
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("  ", "\t")]
    public void SecretSource_Resolve_NeitherSet_ReturnsNull(string? value, string? file)
    {
        // Arrange: nothing to set up

        // Act
        var secret = SecretSource.Resolve(value, ValueSetting, file, FileSetting);

        // Assert
        Assert.Null(secret);
    }

    /// <summary>
    ///     Test that an inline value together with a file is refused, naming both settings and not the secret.
    /// </summary>
    [Fact]
    public void SecretSource_Resolve_BothSet_ThrowsNamingBothSettingsWithoutTheSecret()
    {
        // Arrange: a file that exists and an inline value
        using var directory = new TemporaryDirectory();
        var path = WriteFile(directory, "from-the-file");

        // Act
        var exception = Assert.Throws<InvalidOperationException>(
            () => SecretSource.Resolve(Secret, ValueSetting, path, FileSetting));

        // Assert: both settings are named, no value is shown, and the file was not needed
        Assert.Contains(ValueSetting, exception.Message, StringComparison.Ordinal);
        Assert.Contains(FileSetting, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("from-the-file", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a file that does not exist is refused, naming the setting and the file.
    /// </summary>
    [Fact]
    public void SecretSource_Resolve_MissingFile_ThrowsNamingTheSettingAndTheFile()
    {
        // Arrange: a path in an existing directory where no file exists
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.DirectoryPath, "missing.txt");

        // Act
        var exception = Assert.Throws<InvalidOperationException>(
            () => SecretSource.Resolve(null, ValueSetting, path, FileSetting));

        // Assert
        Assert.Contains(FileSetting, exception.Message, StringComparison.Ordinal);
        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
        Assert.Contains("does not exist", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a path that is a directory, and so cannot be read as a file, is refused, naming the setting.
    /// </summary>
    [Fact]
    public void SecretSource_Resolve_DirectoryInsteadOfFile_ThrowsNamingTheSetting()
    {
        // Arrange: the temporary directory itself
        using var directory = new TemporaryDirectory();

        // Act
        var exception = Assert.Throws<InvalidOperationException>(
            () => SecretSource.Resolve(null, ValueSetting, directory.DirectoryPath, FileSetting));

        // Assert
        Assert.Contains(FileSetting, exception.Message, StringComparison.Ordinal);
        Assert.Contains("cannot be read", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an empty file, and a file with only white space, are refused.
    /// </summary>
    /// <param name="text">The contents of the file.</param>
    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData(" \t\r\n ")]
    public void SecretSource_Resolve_EmptyFile_ThrowsNamingTheSetting(string text)
    {
        // Arrange
        using var directory = new TemporaryDirectory();
        var path = WriteFile(directory, text);

        // Act
        var exception = Assert.Throws<InvalidOperationException>(
            () => SecretSource.Resolve(null, ValueSetting, path, FileSetting));

        // Assert
        Assert.Contains(FileSetting, exception.Message, StringComparison.Ordinal);
        Assert.Contains("empty", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a file one byte past the size limit is refused, and that nothing of it is in the message.
    /// </summary>
    [Fact]
    public void SecretSource_Resolve_FilePastSizeLimit_ThrowsWithoutTheContents()
    {
        // Arrange: a file that starts with a recognizable secret and is one byte too large
        using var directory = new TemporaryDirectory();
        var path = WriteFile(directory, Secret + new string('k', SecretSource.MaxFileBytes - Secret.Length + 1));

        // Act
        var exception = Assert.Throws<InvalidOperationException>(
            () => SecretSource.Resolve(null, ValueSetting, path, FileSetting));

        // Assert
        Assert.Contains(FileSetting, exception.Message, StringComparison.Ordinal);
        Assert.Contains("larger than", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a file that is not UTF-8 text is refused without quoting its bytes.
    /// </summary>
    [Fact]
    public void SecretSource_Resolve_FileNotUtf8_ThrowsWithoutTheContents()
    {
        // Arrange: a recognizable secret followed by bytes that are not valid UTF-8
        using var directory = new TemporaryDirectory();
        var path = WriteFile(directory, [.. Encoding.UTF8.GetBytes(Secret), 0xFF, 0xFE, 0xC3, 0x28]);

        // Act
        var exception = Assert.Throws<InvalidOperationException>(
            () => SecretSource.Resolve(null, ValueSetting, path, FileSetting));

        // Assert: the message names the setting; neither the secret, nor the byte values, nor an inner exception
        Assert.Contains(FileSetting, exception.Message, StringComparison.Ordinal);
        Assert.Contains("UTF-8", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("0xFF", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(exception.InnerException);
    }

    /// <summary>
    ///     Test that a null name of a setting is refused.
    /// </summary>
    [Fact]
    public void SecretSource_Resolve_NullSettingName_ThrowsArgumentNullException()
    {
        // Arrange / Act / Assert: each name is checked
        Assert.Throws<ArgumentNullException>(() => SecretSource.Resolve(null, null!, null, FileSetting));
        Assert.Throws<ArgumentNullException>(() => SecretSource.Resolve(null, ValueSetting, null, null!));
    }
}
