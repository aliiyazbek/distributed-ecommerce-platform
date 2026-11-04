import { useState } from "react";

export default function SignUp({ accounts, onCreate, goSignIn }) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");

  const submit = (e) => {
    e.preventDefault();
    const valid = password.length >= 6 && /[A-Z]/.test(password) && /\d/.test(password);
    if (!valid) return setError("Password needs at least 6 characters, one uppercase letter and one number.");
    if (accounts.some((a) => a.email === email.trim().toLowerCase())) return setError("An account with this email already exists.");
    onCreate({ email: email.trim().toLowerCase(), password });
  };

  return (
    <div className="auth-wrap">
      <form className="card auth" onSubmit={submit}>
        <h2>Create account</h2>
        <label htmlFor="su-email">Email</label>
        <input id="su-email" type="email" placeholder="you@example.com" value={email} onChange={(e) => setEmail(e.target.value)} required />
        <label htmlFor="su-pass">Password</label>
        <input id="su-pass" type="password" placeholder="••••••••" value={password} onChange={(e) => setPassword(e.target.value)} required />
        <p className="hint">At least 6 characters, with one uppercase letter and one number.</p>
        {error && <p className="error">{error}</p>}
        <button className="btn" type="submit">Create account</button>
        <div className="auth-foot">
          Already have an account? <button type="button" className="link" onClick={goSignIn}>Sign in</button>
        </div>
      </form>
    </div>
  );
}
