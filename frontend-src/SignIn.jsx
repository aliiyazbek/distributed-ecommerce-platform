import { useState } from "react";

export default function SignIn({ accounts, onSuccess, goSignUp }) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");

  const submit = (e) => {
    e.preventDefault();
    const acc = accounts.find((a) => a.email === email.trim().toLowerCase() && a.password === password);
    if (!acc) return setError("Email or password is incorrect.");
    onSuccess({ email: acc.email });
  };

  return (
    <div className="auth-wrap">
      <form className="card auth" onSubmit={submit}>
        <h2>Sign in</h2>
        <label htmlFor="si-email">Email</label>
        <input id="si-email" type="email" placeholder="you@example.com" value={email} onChange={(e) => setEmail(e.target.value)} required />
        <label htmlFor="si-pass">Password</label>
        <input id="si-pass" type="password" placeholder="••••••••" value={password} onChange={(e) => setPassword(e.target.value)} required />
        {error && <p className="error">{error}</p>}
        <button className="btn" type="submit">Sign in</button>
        <div className="auth-foot">
          Don't have an account? <button type="button" className="link" onClick={goSignUp}>Create one</button>
        </div>
      </form>
    </div>
  );
}
