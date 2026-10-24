import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { useLookups } from "@/hooks/useReference";
import type { LookupCategory } from "@/types/db";

const NONE = "__none__";

export interface Option {
  value: string;
  label: string;
}

interface BaseProps {
  value: string | null | undefined;
  onChange: (value: string | null) => void;
  placeholder?: string;
  allowClear?: boolean;
  className?: string;
  id?: string;
}

/** Generic value/label select that tolerates values missing from the option list. */
export function OptionSelect({
  value, onChange, options, placeholder = "Select…", allowClear, className, id,
}: BaseProps & { options: Option[] }) {
  const known = options.some((o) => o.value === value);
  const merged = !known && value ? [...options, { value, label: value }] : options;

  return (
    <Select
      value={value ?? NONE}
      onValueChange={(v) => onChange(v === NONE ? null : v)}
    >
      <SelectTrigger id={id} className={className}>
        <SelectValue placeholder={placeholder} />
      </SelectTrigger>
      <SelectContent>
        {allowClear && <SelectItem value={NONE}>—</SelectItem>}
        {merged.map((o) => (
          <SelectItem key={o.value} value={o.value}>
            {o.label}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}

/** Select backed by an editable lookup category. */
export function LookupSelect({
  category, ...rest
}: BaseProps & { category: LookupCategory }) {
  const { data = [] } = useLookups(category);
  return <OptionSelect {...rest} options={data.map((l) => ({ value: l.label, label: l.label }))} />;
}
