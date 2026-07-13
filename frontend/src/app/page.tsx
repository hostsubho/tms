export default function Home() {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center bg-zinc-50 px-4 text-center">
      <p className="text-sm font-medium uppercase tracking-wide text-zinc-500">
        Enterprise Ticket Management
      </p>
      <h1 className="mt-2 text-4xl font-semibold tracking-tight text-zinc-900">TMS</h1>
      <p className="mt-4 max-w-md text-zinc-600">
        Multi-tenant ticketing built to scale from your first customer to your
        hundredth.
      </p>

      <div className="mt-8 flex flex-col gap-3 sm:flex-row">
        <a
          href="/signup"
          className="rounded-md bg-zinc-900 px-5 py-2.5 text-sm font-medium text-white hover:bg-zinc-800"
        >
          Start free trial
        </a>
        <a
          href="/login"
          className="rounded-md border border-zinc-300 px-5 py-2.5 text-sm font-medium text-zinc-700 hover:bg-zinc-100"
        >
          Sign in
        </a>
      </div>

      <div className="mt-10 flex gap-4 text-xs text-zinc-400">
        <a href="/portal/login" className="hover:text-zinc-600">
          Customer support portal
        </a>
        <span>·</span>
        <a href="/admin/login" className="hover:text-zinc-600">
          Platform admin sign in
        </a>
      </div>
    </main>
  );
}
