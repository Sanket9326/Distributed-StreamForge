-- @common
expect(KEYS[1], 'string'); expect(KEYS[2], 'string')
if #ARGV ~= 1 or #ARGV[1] == 0 or #ARGV[1] > 64 then error('INVALID_TOKEN') end
redis.call('SET', KEYS[2], ARGV[1])
return redis.call('DEL', KEYS[1])
