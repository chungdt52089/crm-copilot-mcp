(function () {
  "use strict";

  const form = document.getElementById("login-form");
  const userInput = document.getElementById("login-user");
  const passwordInput = document.getElementById("login-password");
  const button = document.getElementById("login-button");
  const errorBox = document.getElementById("login-error");

  function showError(message) {
    // textContent only — same rule as app.js: no innerHTML with an interpolated value.
    errorBox.textContent = message;
    errorBox.hidden = false;
  }

  form.addEventListener("submit", async function (event) {
    event.preventDefault();
    errorBox.hidden = true;
    button.disabled = true;

    try {
      const res = await fetch("/api/auth/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ userId: userInput.value, password: passwordInput.value }),
      });

      if (res.ok) {
        window.location.href = "/";
        return;
      }

      let message = "Đăng nhập không thành công.";
      try {
        const body = await res.json();
        if (body && typeof body.message === "string") {
          message = body.message;
        }
      } catch (ignored) {
        // non-JSON error body — keep the generic message
      }
      showError(message);
    } catch (err) {
      showError("Không thể kết nối tới máy chủ.");
    } finally {
      button.disabled = false;
    }
  });
})();
