-- @common
-- @history
expect(KEYS[1], 'hash'); expect(KEYS[2], 'zset'); expect(KEYS[3], 'string'); expect(KEYS[4], 'string')
local raw = redis.call('HGET', KEYS[1], ARGV[1])
if raw then
  local s = history_decode(raw)
  if not s.confirmed and s.sourcePartition == tonumber(ARGV[2]) and s.sourceOffset == ARGV[3] then
    redis.call('HDEL', KEYS[1], ARGV[1])
    redis.call('ZREM', KEYS[2], s.sortTime .. ':' .. string.gsub(ARGV[1], '-', ''))
    redis.call('DEL', KEYS[3]); redis.call('SET', KEYS[4], ARGV[4])
  end
end
return 1
