window.quantumWorkspace = {
  getSessionToken: () => sessionStorage.getItem("quantum.platform.token"),
  setSessionToken: token => sessionStorage.setItem("quantum.platform.token", token),
  clearSessionToken: () => sessionStorage.removeItem("quantum.platform.token"),
  downloadText: (name, content) => {
    const blob = new Blob([content], { type: "application/json;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = name;
    anchor.click();
    URL.revokeObjectURL(url);
  },
  copyText: value => navigator.clipboard.writeText(value)
};
