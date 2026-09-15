#!lua flags=no-writes
-- @common
expect(KEYS[1], 'hash')
if #ARGV > 50 then error('INVALID_LIMIT') end
local result = {}
for i = 1, #ARGV do result[i] = redis.call('HGET', KEYS[1], ARGV[i]) or false end
return result
