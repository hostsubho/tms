"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { impersonationBanner, tenantAuth, ImpersonationBanner } from "@/lib/auth";

// Module 5.1 - Tenant impersonation. A layout (not a per-page addition) so
// every page under /dashboard/* gets this banner automatically - no need to
// touch each page individually. Only renders anything when an impersonation
// session is actually active; otherwise this is a no-op passthrough.
export default function DashboardLayout({ children }: { children: React.ReactNode }) {
  const router = useRouter();
  const [banner, setBanner] = useState<ImpersonationBanner | null>(null);

  useEffect(() => {
    setBanner(impersonationBanner.get());
  }, []);

  function handleEndImpersonation() {
    tenantAuth.clear();
    router.replace("/admin/tenants");
  }

  if (!banner) return <>{children}</>;

  return (
    <div>
      <div className="flex items-center justify-between gap-4 bg-amber-500 px-4 py-2 text-sm font-medium text-amber-950">
        <span>
          Impersonating <strong>{banner.targetEmail}</strong> at <strong>{banner.tenantName}</strong> — started by{" "}
          {banner.platformAdminEmail}
        </span>
        <button
          onClick={handleEndImpersonation}
          className="rounded-md bg-amber-950 px-3 py-1 text-xs font-semibold text-amber-50 hover:bg-amber-900"
        >
          End impersonation
        </button>
      </div>
      {children}
    </div>
  );
}
