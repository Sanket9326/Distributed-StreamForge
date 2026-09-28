-- @common
-- @history
expect(KEYS[1], 'hash'); expect(KEYS[2], 'zset'); expect(KEYS[3], 'string'); expect(KEYS[4], 'string')
if redis.call('GET', KEYS[3]) ~= ARGV[1] or redis.call('GET', KEYS[4]) ~= ARGV[1] then return 0 end
-- Validate every entry before mutation: script errors do not roll back earlier commands.
for i = 2, #ARGV, 2 do
  local s = history_decode(ARGV[i+1]); local old = redis.call('HGET', KEYS[1], ARGV[i])
  if old and history_decode(old).sourcePartition ~= s.sourcePartition then error('PARTITION_CHANGED') end
end
for i = 2, #ARGV, 2 do history_write(KEYS[1], KEYS[2], ARGV[i], history_decode(ARGV[i+1])) end
return 1
