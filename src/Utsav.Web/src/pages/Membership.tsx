import { HandCoins } from "lucide-react";
import { SubscriptionsSection } from "./subscriptions/SubscriptionsSection";

export function MembershipPage() {
  return (
    <SubscriptionsSection
      category="membership"
      title="Membership"
      description="Household subscription collection"
      icon={<HandCoins />}
      exportName="membership"
      addLabel="Add donor"
    />
  );
}
