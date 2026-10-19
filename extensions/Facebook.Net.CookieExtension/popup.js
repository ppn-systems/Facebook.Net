// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

document.getElementById("copyBtn").addEventListener("click", async () => {
  const statusEl = document.getElementById("status");
  try {
    const cookies = await chrome.cookies.getAll({ domain: ".facebook.com" });
    const hasSession = cookies.some((c) => c.name === "c_user") && cookies.some((c) => c.name === "xs");

    if (!hasSession) {
      statusEl.style.color = "#F87171";
      statusEl.textContent = "Chưa đăng nhập Facebook trên trình duyệt này! / Please sign in to Facebook first!";
      return;
    }

    const payload = {
      url: "https://www.facebook.com",
      userAgent: navigator.userAgent,
      cookies: cookies.map((c) => ({
        domain: c.domain,
        expirationDate: c.expirationDate,
        hostOnly: c.hostOnly,
        httpOnly: c.httpOnly,
        name: c.name,
        path: c.path,
        sameSite: c.sameSite,
        secure: c.secure,
        session: c.session,
        storeId: c.storeId,
        value: c.value
      }))
    };

    await navigator.clipboard.writeText(JSON.stringify(payload, null, 2));
    statusEl.style.color = "#34D399";
    statusEl.textContent = "✔ Đã sao chép! Hãy bấm 'Dán 1-Click từ Clipboard' trên ứng dụng.";
  } catch (err) {
    statusEl.style.color = "#F87171";
    statusEl.textContent = "Lỗi sao chép / Copy failed: " + (err && err.message ? err.message : String(err));
  }
});
