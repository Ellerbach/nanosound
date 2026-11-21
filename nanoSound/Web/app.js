const language = (navigator.language || "en").toLowerCase().indexOf("fr") === 0 ? "fr" : "en";
document.documentElement.lang = language;
const text = {
  en:{title:"Sound groups",subtitle:"Discovered player library",volume:"Volume",sync:"Sync clock",reset:"Reset all",ssid:"Access point name",password:"New password (blank keeps current)",random:"Random playback",min:"Minimum delay (s)",max:"Maximum delay (s)",name:"Name",schedule:"Fixed times",play:"Play",queued:"Sound queued.",synced:"Clock synchronized.",resetConfirm:"Reset all settings, including the network name and password? You must power-cycle the device afterward.",resetDone:"Defaults restored. Power-cycle the device once, then reconnect.",loadError:"Could not load the sound library.",empty:"No folders were discovered.",scheduleHint:"HH:mm or *:mm, comma-separated"},
  fr:{title:"Groupes de sons",subtitle:"Bibliothèque détectée du lecteur",volume:"Volume",sync:"Synchroniser l'heure",reset:"Tout réinitialiser",ssid:"Nom du point d'accès",password:"Nouveau mot de passe (vide = inchangé)",random:"Lecture aléatoire",min:"Délai minimum (s)",max:"Délai maximum (s)",name:"Nom",schedule:"Heures fixes",play:"Lire",queued:"Son ajouté à la file.",synced:"Heure synchronisée.",resetConfirm:"Réinitialiser tous les paramètres, y compris le nom du réseau et le mot de passe ? Vous devrez ensuite couper puis rétablir l'alimentation.",resetDone:"Valeurs par défaut restaurées. Coupez puis rétablissez l'alimentation, puis reconnectez-vous.",loadError:"Impossible de charger la bibliothèque.",empty:"Aucun dossier détecté.",scheduleHint:"HH:mm ou *:mm, séparés par des virgules"}
}[language];
const element = id => document.getElementById(id);
const setText = (id,value) => {
  const target = element(id);
  if (target) target.textContent = value;
};
document.title = text.title;
setText("title",text.title);
setText("subtitle",text.subtitle);
setText("volumeLabel",text.volume);
setText("syncButton",text.sync);
setText("resetButton",text.reset);
setText("ssidLabel",text.ssid);
setText("passwordLabel",text.password);
element("volume").oninput = () => element("volumeValue").value = element("volume").value;

function showMessage(message,error) {
  element("status").textContent = message;
  element("status").style.color = error ? "#a7353f" : "#187568";
}

async function request(url) {
  const response = await fetch(url,{cache:"no-store"});
  if (!response.ok) throw new Error(await response.text());
  return response.json();
}

function field(label,value,type,className) {
  const wrapper = document.createElement("label");
  if (className) wrapper.className = className;
  wrapper.append(document.createTextNode(label));
  const input = document.createElement("input");
  input.type = type || "text";
  input.value = value;
  wrapper.append(input);
  return {wrapper,input};
}

function render(state) {
  element("volume").value = state.volume;
  element("volumeValue").value = state.volume;
  element("ssid").value = state.ssid;
  element("password").value = "";
  const catalog = element("catalog");
  catalog.textContent = "";
  if (!state.groups.length) {
    const empty = document.createElement("div");
    empty.className = "empty";
    empty.textContent = text.empty;
    catalog.append(empty);
    return;
  }

  state.groups.forEach(group => {
    const section = document.createElement("section");
    section.className = "group";
    section.setAttribute("data-folder",group.folder);
    const head = document.createElement("div");
    head.className = "group-head";
    const name = field(text.name,group.name,"text","group-name");
    const randomLabel = document.createElement("label");
    randomLabel.className = "check";
    const random = document.createElement("input");
    random.type = "checkbox";
    random.checked = group.random;
    randomLabel.append(random,document.createTextNode(text.random));
    const minimum = field(text.min,group.min,"number","group-min");
    minimum.input.min = 0; minimum.input.max = 86400;
    const maximum = field(text.max,group.max,"number","group-max");
    maximum.input.min = 0; maximum.input.max = 86400;
    head.append(name.wrapper,randomLabel,minimum.wrapper,maximum.wrapper);
    section.append(head);

    const tracks = document.createElement("div");
    tracks.className = "tracks";
    group.tracks.forEach(track => {
      const row = document.createElement("div");
      row.className = "track";
      row.setAttribute("data-track",track.track);
      const number = document.createElement("div");
      number.className = "number";
      number.textContent = track.track;
      const trackName = field(text.name,track.name,"text","track-name");
      const schedule = field(text.schedule,track.schedule,"text","schedule");
      schedule.input.placeholder = text.scheduleHint;
      schedule.input.title = text.scheduleHint;
      const play = document.createElement("button");
      play.className = "play";
      play.textContent = "\u25b6";
      play.title = text.play;
      play.onclick = async () => {
        try { await request("/api/play?folder=" + group.folder + "&track=" + track.track); showMessage(text.queued); }
        catch(error) { showMessage(error.message,true); }
      };
      row.append(number,trackName.wrapper,schedule.wrapper,play);
      tracks.append(row);
    });
    section.append(tracks);
    catalog.append(section);
  });
}

async function load() {
  try { render(await request("/api/state")); }
  catch(error) { showMessage(text.loadError + " " + error.message,true); }
}

async function syncTime() {
  try {
    await request("/api/time?epoch=" + Math.floor(Date.now()/1000) + "&offset=" + new Date().getTimezoneOffset());
    showMessage(text.synced);
  } catch(error) { showMessage(error.message,true); }
}

async function resetAll() {
  if (!confirm(text.resetConfirm)) return;
  try { await request("/api/reset"); showMessage(text.resetDone); }
  catch(error) { showMessage(error.message,true); }
}

load();