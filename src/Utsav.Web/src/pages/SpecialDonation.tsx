import { Sparkles } from "lucide-react";
import { SubscriptionsSection } from "./subscriptions/SubscriptionsSection";

export function SpecialDonationPage() {
  return (
    <SubscriptionsSection
      category="special"
      title="Special Donation"
      description="Major gifts, tracked like membership but without the yearly raise"
      icon={<Sparkles />}
      exportName="special-donation"
      addLabel="Add donor"
    />
  );
}
