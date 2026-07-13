export default function SuperAdminTenantsPage() {
  return (
    <main className="p-8">
      <h1 className="text-2xl font-semibold">Tenants</h1>
      <p className="text-sm text-gray-500 mt-2">
        Super Admin: list/search/create/suspend tenants. See Module 5.1
        (Tenant Lifecycle Management) in the feature spec. This route group
        should be gated by platform-admin auth only, never tenant sessions.
      </p>
    </main>
  );
}
