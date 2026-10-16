/* global Reveal, RevealMarkdown, RevealHighlight, RevealNotes */
if (typeof Reveal === "undefined") {
  document.getElementById("dependency-error").hidden = false;
} else {
  Reveal.initialize({
    width: 1200,
    height: 760,
    margin: 0.06,
    hash: true,
    center: false,
    slideNumber: "c/t",
    transition: "none",
    backgroundTransition: "none",
    controlsTutorial: false,
    plugins: [RevealMarkdown, RevealHighlight, RevealNotes]
  }).then(() => {
    for (const link of document.querySelectorAll(".slides a[href]")) {
      if (link.getAttribute("href").startsWith("#")) continue;
      link.target = "_blank";
      link.rel = "noopener";
    }
  });
}
