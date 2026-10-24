using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Utsav.Infrastructure.Data.Entities;
using Xunit;

namespace Utsav.IsolationTests;

/// <summary>
/// Documents live in Blob storage, outside the SQL database, so row-level
/// security does not protect the bytes. The tenant boundary there is the blob
/// name prefix ({orgId}/...), and these tests hold the app to it.
/// </summary>
[Collection(nameof(DatabaseFixture))]
public class DocumentIsolationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task A_committee_can_download_its_own_document()
    {
        using var asA = fixture.ClientFor(fixture.TokenForA);

        var response = await asA.GetAsync($"/api/documents/{fixture.A.Document}/download");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("PDF-BYTES-ZZA", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Downloading_another_committees_document_by_id_is_refused()
    {
        using var asA = fixture.ClientFor(fixture.TokenForA);

        var response = await asA.GetAsync($"/api/documents/{fixture.B.Document}/download");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("ZZB", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_row_whose_stored_blob_belongs_to_another_committee_is_not_served()
    {
        // The row lookup is tenant-scoped, but the bytes are fetched by the
        // stored path. If a row ever pointed at another committee's blob (a
        // bad import, a bug, tampering), the download must not become a way to
        // read it.
        var rogue = Guid.NewGuid();
        await using (var inC = fixture.ContextFor(fixture.TenantC))
        {
            inC.Documents.Add(new Document
            {
                Id = rogue, OrgId = fixture.TenantC, Title = "points at B's blob", FileName = "stolen.pdf",
                BlobName = $"{fixture.TenantB}/receipt-ZZB.pdf", ContentType = "application/pdf", SizeBytes = 1,
            });
            await inC.SaveChangesAsync();
        }

        using var asC = fixture.ClientFor(fixture.TokenForC);
        var response = await asC.GetAsync($"/api/documents/{rogue}/download");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("PDF-BYTES-ZZB", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Deleting_a_row_never_deletes_a_blob_outside_the_callers_prefix()
    {
        var rogue = Guid.NewGuid();
        var victim = $"{fixture.TenantB}/receipt-ZZB.pdf";
        await using (var inC = fixture.ContextFor(fixture.TenantC))
        {
            inC.Documents.Add(new Document
            {
                Id = rogue, OrgId = fixture.TenantC, Title = "delete me", FileName = "x.pdf",
                BlobName = victim, ContentType = "application/pdf", SizeBytes = 1,
            });
            await inC.SaveChangesAsync();
        }

        using var asC = fixture.ClientFor(fixture.TokenForC);
        await asC.DeleteAsync($"/api/documents/{rogue}");

        Assert.Contains(victim, fixture.Blobs.Names);
    }

    [Fact]
    public async Task Deleting_another_committees_document_by_id_removes_neither_row_nor_blob()
    {
        using var asA = fixture.ClientFor(fixture.TokenForA);

        var response = await asA.DeleteAsync($"/api/documents/{fixture.B.Document}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var inB = fixture.ContextFor(fixture.TenantB);
        Assert.True(await inB.Documents.AnyAsync(d => d.Id == fixture.B.Document));
        Assert.Contains($"{fixture.TenantB}/receipt-ZZB.pdf", fixture.Blobs.Names);
    }

    [Fact]
    public async Task An_upload_is_stored_under_the_callers_prefix_and_invisible_to_the_other_committee()
    {
        using var asC = fixture.ClientFor(fixture.TokenForC);
        using var form = new MultipartFormDataContent
        {
            { new StringContent("Upload ZZC-only"), "title" },
        };
        var file = new ByteArrayContent("UPLOADED-BY-A"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "mine.pdf");

        var response = await asC.PostAsync("/api/documents", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Contains(fixture.Blobs.Names, n => n.StartsWith($"{fixture.TenantC}/", StringComparison.Ordinal) && n.EndsWith("mine.pdf", StringComparison.Ordinal));
        Assert.DoesNotContain(fixture.Blobs.Names, n => n.StartsWith($"{fixture.TenantA}/", StringComparison.Ordinal) && n.EndsWith("mine.pdf", StringComparison.Ordinal));
        Assert.DoesNotContain(fixture.Blobs.Names, n => n.StartsWith($"{fixture.TenantB}/", StringComparison.Ordinal) && n.EndsWith("mine.pdf", StringComparison.Ordinal));

        using var asA = fixture.ClientFor(fixture.TokenForA);
        using var asB = fixture.ClientFor(fixture.TokenForB);
        Assert.DoesNotContain("Upload ZZC-only", await (await asA.GetAsync("/api/documents")).Content.ReadAsStringAsync());
        Assert.DoesNotContain("Upload ZZC-only", await (await asB.GetAsync("/api/documents")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_upload_cannot_attach_itself_to_another_committees_contract()
    {
        using var asA = fixture.ClientFor(fixture.TokenForA);
        using var form = new MultipartFormDataContent
        {
            { new StringContent("Cross-linked"), "title" },
            { new StringContent(fixture.B.Contract.ToString()), "contract_id" },
        };
        var file = new ByteArrayContent("x"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "link.pdf");

        var response = await asA.PostAsync("/api/documents", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
