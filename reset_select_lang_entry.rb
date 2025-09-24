# frozen_string_literal: true

require "json"

keys_to_remove = %w[big_suffix1 frost_suffix1 fire_suffix1 poison_suffix1 lightning_suffix1 bossSummoner_suffix1 Splitter_suffix1 Lootbags_suffix1]

language_files = Dir["StarLevelSystem/Localization/*"]
language_files.each do |lang_file|
  next if lang_file == "StarLevelSystem/Localization/English.json"

  lang_json = JSON.parse(File.read("#{lang_file}"))
  puts "Removing keys from #{lang_file}"
  keys_to_remove.each do |rm_key|
    lang_json.delete(rm_key)
  end
  File.open("#{lang_file}", "w") { |f| f.write(JSON.pretty_generate(lang_json)) }
end
