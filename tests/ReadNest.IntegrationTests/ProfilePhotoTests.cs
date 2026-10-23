using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ReadNest.Application.Social;
using ReadNest.IntegrationTests.Infrastructure;
using SkiaSharp;

namespace ReadNest.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class ProfilePhotoTests(ReadNestFactory factory)
{
    private static MultipartFormDataContent Photo(byte[] content, string name = "me.png", string type = "image/png")
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(type);
        return new MultipartFormDataContent { { file, "photo", name } };
    }

    private async Task<string> UploadAsync(TestUser user, byte[] content)
    {
        var response = await user.Client.PutAsync("/api/v1/profile/photo", Photo(content));
        var body = JsonDocument.Parse(await response.ReadSuccessStringAsync()).RootElement;
        Assert.True(body.GetProperty("hasUploadedPhoto").GetBoolean());
        return body.GetProperty("profileImageUrl").GetString()!;
    }

    private string[] StoredPhotos() =>
        Directory.Exists(Path.Combine(factory.StorageRoot, "photos"))
            ? Directory.GetFiles(Path.Combine(factory.StorageRoot, "photos"), "*.webp", SearchOption.AllDirectories)
            : [];

    [Fact]
    public async Task Uploaded_photo_is_reencoded_and_served_to_its_owner()
    {
        var user = await factory.CreateUserAsync();

        var url = await UploadAsync(user, TestFiles.Image(900, 600, SKEncodedImageFormat.Jpeg));

        Assert.Matches($"^api/v1/users/{user.Id:N}/photo\\?v=[0-9]+$", url);
        var photo = await user.Client.GetAsync("/" + url);
        Assert.Equal(HttpStatusCode.OK, photo.StatusCode);
        Assert.Equal("image/webp", photo.Content.Headers.ContentType!.MediaType);
        Assert.Contains("private", photo.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        var bytes = await photo.Content.ReadAsByteArrayAsync();
        using var codec = SKCodec.Create(new MemoryStream(bytes));
        Assert.Equal(SKEncodedImageFormat.Webp, codec.EncodedFormat);
        Assert.Equal(512, codec.Info.Width);
        Assert.Equal(512, codec.Info.Height);

        // The profile carries a key, never the image itself.
        var profile = await factory.WithServiceAsync<ProfileDto?, ProfileService>(s => s.GetAsync(user.Id, CancellationToken.None));
        Assert.Equal(url, profile!.ProfileImageUrl);
    }

    [Theory]
    [MemberData(nameof(Rejected))]
    public async Task Unsupported_spoofed_and_oversized_images_are_rejected(byte[] content, string name, string type)
    {
        var user = await factory.CreateUserAsync();

        var response = await user.Client.PutAsync("/api/v1/profile/photo", Photo(content, name, type));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var profile = await factory.WithServiceAsync<ProfileDto?, ProfileService>(s => s.GetAsync(user.Id, CancellationToken.None));
        Assert.False(profile!.HasUploadedPhoto);
    }

    public static TheoryData<byte[], string, string> Rejected() => new()
    {
        { TestFiles.Svg(), "me.svg", "image/svg+xml" },
        { TestFiles.Svg(), "me.png", "image/png" },
        { TestFiles.Pdf(), "me.jpg", "image/jpeg" },
        { TestFiles.PngBomb(), "bomb.png", "image/png" },
        { new byte[ReadNestFactory.UploadLimit + 10], "huge.png", "image/png" },
        { "GIF89a"u8.ToArray(), "me.gif", "image/gif" },
    };

    [Fact]
    public async Task A_failed_replacement_keeps_the_current_photo()
    {
        var user = await factory.CreateUserAsync();
        var url = await UploadAsync(user, TestFiles.Image(300, 300));

        var bad = await user.Client.PutAsync("/api/v1/profile/photo", Photo(TestFiles.Svg(), "x.png"));

        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.Client.GetAsync("/" + url)).StatusCode);
    }

    [Fact]
    public async Task Replacing_and_removing_delete_the_stored_files()
    {
        var user = await factory.CreateUserAsync();
        var before = StoredPhotos().Length;

        var first = await UploadAsync(user, TestFiles.Image(300, 300));
        var second = await UploadAsync(user, TestFiles.Image(400, 400, SKEncodedImageFormat.Webp));

        Assert.NotEqual(first, second);
        Assert.Equal(before + 1, StoredPhotos().Length);

        var removed = JsonDocument.Parse(await (await user.Client.DeleteAsync("/api/v1/profile/photo")).ReadSuccessStringAsync()).RootElement;
        Assert.Equal(JsonValueKind.Null, removed.GetProperty("profileImageUrl").ValueKind);
        Assert.Equal(before, StoredPhotos().Length);
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.GetAsync("/" + second)).StatusCode);
    }

    [Fact]
    public async Task Private_profile_photos_are_only_visible_to_their_owner()
    {
        var owner = await factory.CreateUserAsync();
        var stranger = await factory.CreateUserAsync();
        var url = "/" + await UploadAsync(owner, TestFiles.Image(200, 200));
        var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await owner.Client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(url)).StatusCode);

        await factory.WithServiceAsync<ProfileService>(s => s.UpdatePrivacyAsync(owner.Id, new PrivacyRequest(true, false), CancellationToken.None));

        Assert.Equal(HttpStatusCode.OK, (await stranger.Client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Each_user_can_only_change_their_own_photo()
    {
        var alice = await factory.CreateUserAsync();
        var bob = await factory.CreateUserAsync();
        var aliceUrl = "/" + await UploadAsync(alice, TestFiles.Image(200, 200));

        // Bob's upload and removal only ever affect Bob's profile: the user comes from the token, not the request.
        await UploadAsync(bob, TestFiles.Image(250, 250));
        (await bob.Client.DeleteAsync("/api/v1/profile/photo")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await alice.Client.GetAsync(aliceUrl)).StatusCode);
    }

    [Fact]
    public async Task Photo_upload_requires_a_bearer_token()
    {
        var anonymous = factory.CreateClient();

        var response = await anonymous.PutAsync("/api/v1/profile/photo", Photo(TestFiles.Image(100, 100)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Editing_the_profile_keeps_an_uploaded_photo()
    {
        var user = await factory.CreateUserAsync();
        var url = await UploadAsync(user, TestFiles.Image(200, 200));
        var profile = (await factory.WithServiceAsync<ProfileDto?, ProfileService>(s => s.GetAsync(user.Id, CancellationToken.None)))!;

        // Sending the profile back unchanged (including the photo URL it was given) keeps the photo.
        var updated = await factory.WithServiceAsync<ProfileDto, ProfileService>(s =>
            s.UpdateProfileAsync(user.Id, new ProfileUpdateRequest("New Name", profile.Handle, "Bio", profile.ProfileImageUrl), CancellationToken.None));

        Assert.Equal(url, updated.ProfileImageUrl);
        Assert.True(updated.HasUploadedPhoto);
    }
}
