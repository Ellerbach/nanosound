const configText = language === "fr"
  ? {download:"Télécharger la configuration",upload:"Importer la configuration",save:"Tout enregistrer",saved:"Tous les réglages sont enregistrés.",networkSaved:"Tous les réglages sont enregistrés. Coupez puis rétablissez l'alimentation pour appliquer le réseau.",invalidNetwork:"Le nom du réseau doit contenir 1 à 32 caractères et le nouveau mot de passe 8 à 63 caractères.",imported:"Configuration importée. Coupez puis rétablissez l'alimentation si les paramètres réseau ont changé.",error:"Impossible d'enregistrer la configuration."}
  : {download:"Download config",upload:"Upload config",save:"Save all",saved:"All settings saved.",networkSaved:"All settings saved. Power-cycle the device to apply the network.",invalidNetwork:"Network name must be 1-32 characters and a new password 8-63 characters.",imported:"Configuration imported. Power-cycle the device if the network settings changed.",error:"Could not save the configuration."};

setText("downloadButton",configText.download);
setText("uploadButton",configText.upload);
document.querySelectorAll(".save-all").forEach(button => button.textContent = configText.save);

function encodeConfiguration(value) {
  return btoa(encodeURIComponent(value).replace(/%([0-9A-F]{2})/g,(match,hex) => String.fromCharCode(parseInt(hex,16))));
}

async function saveAll() {
  const buttons = document.querySelectorAll(".save-all");
  buttons.forEach(button => button.disabled = true);
  try {
    const currentResponse = await fetch("/api/config",{cache:"no-store"});
    if (!currentResponse.ok) throw new Error(await currentResponse.text());
    const settings = (await currentResponse.text()).split("\n").find(line => line.indexOf("S|") === 0).split("|");
    const ssid = element("ssid").value.trim();
    const password = element("password").value;
    if (!ssid || ssid.length > 32 || (password && (password.length < 8 || password.length > 63))) throw new Error(configText.invalidNetwork);

    const lines = ["V|2","S|" + element("volume").value + "|" + settings[2] + "|" + encodeConfiguration(ssid) + "|" + (password ? encodeConfiguration(password) : settings[4])];
    document.querySelectorAll(".group").forEach(group => {
      const folder = group.getAttribute("data-folder");
      const tracks = group.querySelectorAll(".track");
      lines.push("G|" + folder + "|" + encodeConfiguration(group.querySelector(".group-name input").value) + "|" + (group.querySelector(".check input").checked ? 1 : 0) + "|" + group.querySelector(".group-min input").value + "|" + group.querySelector(".group-max input").value + "|" + tracks.length);
      tracks.forEach(track => lines.push("T|" + folder + "|" + track.getAttribute("data-track") + "|" + encodeConfiguration(track.querySelector(".track-name input").value) + "|" + encodeConfiguration(track.querySelector(".schedule input").value)));
    });

    const response = await fetch("/api/config",{method:"POST",cache:"no-store",headers:{"Content-Type":"text/plain; charset=utf-8"},body:lines.join("\n") + "\n"});
    if (!response.ok) throw new Error(await response.text());
    element("password").value = "";
    showMessage(password || encodeConfiguration(ssid) !== settings[3] ? configText.networkSaved : configText.saved);
  } catch(error) { showMessage(configText.error + " " + error.message,true); }
  buttons.forEach(button => button.disabled = false);
}

function downloadConfiguration() {
  const link = document.createElement("a");
  link.href = "/api/config";
  link.download = "nanoSound-config.txt";
  document.body.append(link);
  link.click();
  link.remove();
}

function uploadConfiguration(input) {
  if (!input.files || !input.files.length) return;
  const reader = new FileReader();
  reader.onload = async () => {
    try {
      const response = await fetch("/api/config",{method:"POST",cache:"no-store",headers:{"Content-Type":"text/plain; charset=utf-8"},body:reader.result});
      if (!response.ok) throw new Error(await response.text());
      render(await request("/api/state"));
      showMessage(configText.imported);
    } catch(error) { showMessage(configText.error + " " + error.message,true); }
    input.value = "";
  };
  reader.onerror = () => { showMessage(configText.error,true); input.value = ""; };
  reader.readAsText(input.files[0]);
}