import type { FieldErrors, FieldValues, Path, UseFormSetFocus } from "react-hook-form";
import { toast } from "sonner";

/**
 * react-hook-form's onInvalid handler (handleSubmit's 2nd argument). A
 * blocked save used to fail silently — the button just didn't do anything,
 * with no toast and no indication which field was the problem. This surfaces
 * the first invalid field's message as a toast and focuses it, so "why isn't
 * save working" has an obvious answer.
 */
export function onInvalidFocus<T extends FieldValues>(setFocus: UseFormSetFocus<T>) {
  return (errors: FieldErrors<T>) => {
    const firstKey = Object.keys(errors)[0] as Path<T> | undefined;
    if (!firstKey) return;
    const entry = errors[firstKey as keyof typeof errors];
    const message = (entry && typeof entry === "object" && "message" in entry && entry.message)
      ? String(entry.message)
      : "Please fill in the highlighted field";
    toast.error(message);
    setFocus(firstKey);
  };
}
