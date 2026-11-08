# Draft for issue #182. Requires brew audit and tests on both Mac architectures.
cask "hyperfidel" do
  arch arm: "arm64", intel: "x64"

  version "0.2.0"
  sha256 arm:   "02a4b98871c416f3398fb60921622589376fb5fce544e74dd9e98cc4eb39d755",
         intel: "ec633e2ab3ca3a6f043e6e21b37a7586a3009ebe2e819d0b593c428981f6b436"

  url "https://github.com/wuuwuu26/HyperFidel/releases/download/v#{version}/HyperFidel-#{version}-osx-#{arch}-Portable.zip"
  name "HyperFidel"
  desc "Music library and Hi-Fi audio player"
  homepage "https://github.com/wuuwuu26/HyperFidel/"

  app "HyperFidel.app"

  caveats <<~EOS
    HyperFidel is not currently signed with an Apple Developer ID or notarized.
    macOS may require explicit approval in Privacy & Security after an initial
    launch attempt. Review the source and release before granting approval.
    This cask does not disable Gatekeeper or remove quarantine automatically.
  EOS
end
