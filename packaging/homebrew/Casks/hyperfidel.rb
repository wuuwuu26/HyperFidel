# Draft for issue #182. Requires brew audit and tests on both Mac architectures.
cask "hyperfidel" do
  arch arm: "arm64", intel: "x64"

  version "0.48.1"
  sha256 arm:   "228bce9f85dbd152f24aff5000891e87a7a77912426fea3fe99ebc5c10793f02",
         intel: "7fc73d5fd7d0b147196322ebec77df55f4126d715e01ff03084132c19ff0ce37"

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
