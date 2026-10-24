using Utsav.Api.Endpoints;

namespace Utsav.UnitTests;

/// <summary>
/// <see cref="CrudEndpoints.PersistCollectionPaymentsAsync"/> replaces a collection's
/// whole instalment set on every save (delete-and-reinsert, no client-round-tripped
/// row id), so the only way to know which instalments actually changed — the detail
/// History needs to show a settlement as its own entry — is to diff the old list
/// against the new one. <see cref="CrudEndpoints.DiffSettlements"/> is that diff,
/// pulled out as a pure function so it's testable without a database.
/// </summary>
public class CrudEndpointsTests
{
    public class DiffSettlements
    {
        private static CrudEndpoints.SettlementFields F(decimal amount, string? paidOn, string? mode) => new(amount, paidOn, mode);

        [Fact]
        public void A_brand_new_instalment_is_an_add()
        {
            var (added, removed) = CrudEndpoints.DiffSettlements(
                existing: [],
                incoming: [F(500m, "2026-10-11", "Cash")]);

            var only = Assert.Single(added);
            Assert.Equal(500m, only.Amount);
            Assert.Empty(removed);
        }

        [Fact]
        public void An_untouched_instalment_is_neither_added_nor_removed()
        {
            var unchanged = F(500m, "2026-10-11", "Cash");

            var (added, removed) = CrudEndpoints.DiffSettlements(
                existing: [unchanged],
                incoming: [unchanged]);

            Assert.Empty(added);
            Assert.Empty(removed);
        }

        [Fact]
        public void A_dropped_instalment_is_a_remove()
        {
            var dropped = F(300m, "2026-09-01", "UPI");

            var (added, removed) = CrudEndpoints.DiffSettlements(
                existing: [dropped],
                incoming: []);

            Assert.Empty(added);
            var only = Assert.Single(removed);
            Assert.Equal(300m, only.Amount);
        }

        [Fact]
        public void Editing_an_instalments_amount_reads_as_a_remove_plus_an_add()
        {
            // No stable row id survives the round trip, so an edit can't be told
            // apart from "removed the old one, added a new one" — by design.
            var (added, removed) = CrudEndpoints.DiffSettlements(
                existing: [F(500m, "2026-10-11", "Cash")],
                incoming: [F(700m, "2026-10-11", "Cash")]);

            var addedOne = Assert.Single(added);
            Assert.Equal(700m, addedOne.Amount);
            var removedOne = Assert.Single(removed);
            Assert.Equal(500m, removedOne.Amount);
        }

        [Fact]
        public void Two_instalments_with_identical_fields_are_matched_one_for_one()
        {
            // Two genuinely identical rows (same amount/date/mode) shouldn't
            // both get flagged as changed just because they're indistinguishable.
            var same = F(250m, "2026-10-01", "Cash");

            var (added, removed) = CrudEndpoints.DiffSettlements(
                existing: [same, same],
                incoming: [same, same]);

            Assert.Empty(added);
            Assert.Empty(removed);
        }

        [Fact]
        public void ToAuditFields_carries_amount_date_and_mode()
        {
            var fields = F(500m, "2026-10-11", "Cash").ToAuditFields();

            Assert.Equal(500m, fields["amount"]);
            Assert.Equal("2026-10-11", fields["paid_on"]);
            Assert.Equal("Cash", fields["mode"]);
        }
    }
}
